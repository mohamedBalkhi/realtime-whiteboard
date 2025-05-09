using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using RealtimeWhiteboard.Models;
using RealtimeWhiteboard.Data;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Diagnostics; // For Stopwatch

namespace RealtimeWhiteboard.Hubs
{
    // DTO for actions broadcast to clients (includes DB ID)
    // Added Radius to ensure circle data is consistently available.
    public record DrawingAction(int Id, string SessionId, string Type, double X1, double Y1, double X2, double Y2, string? Color, double? LineWidth, string? StrokeDataJson, double? Radius = null); 
    
    // DTO for actions received from the client (before DB ID is assigned)
    public record DrawingActionInput(string SessionId, string Type, double X1, double Y1, double X2, double Y2, string? Color, double? LineWidth, string? StrokeDataJson);
    
    public record CursorPosition(string ConnectionId, double X, double Y);

    public class SessionState
    {
        public List<DrawingElement> CurrentElements { get; set; } = new List<DrawingElement>();
        public Stack<DrawingElement> RedoStack { get; set; } = new Stack<DrawingElement>();
        // Note: Client-side action de-duplication is handled in Home.razor.
        // If server-side de-duplication of client submissions is needed, it could be added here.
    }

    public class WhiteboardHub : Hub
    {
        private readonly ApplicationDbContext _context;
        // Using sessionId as key for SessionState
        private static readonly ConcurrentDictionary<string, SessionState> _sessionManager = new();
        // Using SemaphoreSlim for asynchronous locking per session ID
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();


        public WhiteboardHub(ApplicationDbContext context)
        {
            _context = context;
        }

        public override async Task OnConnectedAsync()
        {
            Console.WriteLine($"HUB LOG: Client {Context.ConnectionId} connected.");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Console.WriteLine($"HUB LOG: Client {Context.ConnectionId} disconnected. Exception: {exception?.Message}");
            // TODO: Consider more sophisticated session cleanup if a user was the last in a session.
            // This might involve tracking connection counts per session group.
            await base.OnDisconnectedAsync(exception);
        }
        
        // Helper to get or create a SemaphoreSlim for a given session ID
        private SemaphoreSlim GetSessionSemaphore(string sessionId)
        {
            return _sessionSemaphores.GetOrAdd(sessionId, sId => new SemaphoreSlim(1, 1));
        }

        // Gets or creates the session state. Handles loading from DB on first access for a session.
        // Uses SemaphoreSlim to ensure thread-safe initialization.
        private async Task<SessionState> GetOrCreateSessionStateAsync(string sessionId)
        {
            // Fast path: if already in dictionary, return it.
            if (_sessionManager.TryGetValue(sessionId, out var existingState))
            {
                return existingState;
            }

            var sessionSemaphore = GetSessionSemaphore(sessionId);
            await sessionSemaphore.WaitAsync(); // Asynchronously wait to enter the critical section for this session
            try
            {
                // Double-check if another thread initialized it while this one was waiting
                if (_sessionManager.TryGetValue(sessionId, out existingState))
                {
                    return existingState;
                }

                Console.WriteLine($"HUB LOG: SessionState for '{sessionId}' not found in memory. Loading from DB or creating new.");
                var stopwatch = Stopwatch.StartNew();
                // Load active elements from DB, ordered by creation time
                var elementsFromDb = await _context.DrawingElements
                                                .Where(e => e.SessionId == sessionId && e.IsActive)
                                                .OrderBy(e => e.CreatedAt) 
                                                .AsNoTracking() // Important for read-only query performance
                                                .ToListAsync();
                stopwatch.Stop();
                Console.WriteLine($"HUB LOG: DB query for initial elements in session '{sessionId}' took {stopwatch.ElapsedMilliseconds}ms. Loaded {elementsFromDb.Count} active elements.");
                
                var newState = new SessionState { CurrentElements = elementsFromDb };
                // Attempt to add the new state to the concurrent dictionary
                if (_sessionManager.TryAdd(sessionId, newState))
                {
                     Console.WriteLine($"HUB LOG: New SessionState created and cached for session '{sessionId}'.");
                }
                else
                {
                    // This case should be rare due to the double-check, but if it happens, use the one that was added by another thread.
                    _sessionManager.TryGetValue(sessionId, out newState);
                     Console.WriteLine($"HUB WARNING: SessionState for '{sessionId}' was added by another thread concurrently. Using existing.");
                }
                return newState!; // newState should not be null here
            }
            finally
            {
                sessionSemaphore.Release(); // Release the semaphore
            }
        }

        public async Task<string> CreateSession()
        {
            var overallSw = Stopwatch.StartNew();
            string sessionId = Guid.NewGuid().ToString("N");
            
            // Ensure SessionState object exists in memory (it will be empty if new)
            await GetOrCreateSessionStateAsync(sessionId); 
            
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            overallSw.Stop();
            Console.WriteLine($"HUB LOG: Client {Context.ConnectionId} created and joined session: {sessionId}. CreateSession took {overallSw.ElapsedMilliseconds}ms.");
            return sessionId;
        }

        public async Task JoinSession(string sessionId)
        {
            var overallSw = Stopwatch.StartNew();
            if (string.IsNullOrEmpty(sessionId))
            {
                Console.WriteLine($"HUB ERROR: Client {Context.ConnectionId} attempted to join an invalid (empty) session ID.");
                // Optionally send error to caller: await Clients.Caller.SendAsync("ErrorJoining", "Session ID cannot be empty.");
                return;
            }

            // Add to SignalR group first
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            Console.WriteLine($"HUB LOG: Client {Context.ConnectionId} added to SignalR group for session: {sessionId}.");

            // Get or load session state. This is internally synchronized.
            SessionState sessionState = await GetOrCreateSessionStateAsync(sessionId);

            // Create history DTOs. This part is read-only on the sessionState.CurrentElements.
            var historyActions = sessionState.CurrentElements.Select(element => new DrawingAction(
                element.Id, element.SessionId, element.ToolType, element.X1, element.Y1, 
                element.X2, element.Y2, element.Color, element.LineWidth, 
                element.StrokeDataJson, element.Radius // Ensure Radius is included
            )).ToList(); // ToList() creates a copy, safe from modification if sessionState changes later.
            
            bool currentCanUndo = sessionState.CurrentElements.Any();
            bool currentCanRedo = sessionState.RedoStack.Any();
            
            // Send history to the caller
            await Clients.Caller.SendAsync("ReceiveSessionHistory", historyActions, currentCanUndo, currentCanRedo);
            overallSw.Stop();
            Console.WriteLine($"HUB LOG: Sent {historyActions.Count} history actions to client {Context.ConnectionId} for session {sessionId}. CanUndo: {currentCanUndo}, CanRedo: {currentCanRedo}. JoinSession took {overallSw.ElapsedMilliseconds}ms.");
        }

        public async Task LeaveSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
            Console.WriteLine($"HUB LOG: Client {Context.ConnectionId} left session: {sessionId}.");
            // Note: SessionState and SemaphoreSlim for this sessionId remain in memory.
            // Consider a cleanup strategy for inactive sessions if memory becomes a concern.
        }

        public async Task SendDrawingAction(DrawingActionInput clientAction)
        {
            var totalProcessingSw = Stopwatch.StartNew();
            if (string.IsNullOrEmpty(clientAction.SessionId))
            {
                Console.WriteLine($"HUB ERROR: SessionId missing in DrawingActionInput from {Context.ConnectionId}. Action ignored.");
                return;
            }

            var sessionSemaphore = GetSessionSemaphore(clientAction.SessionId);
            await sessionSemaphore.WaitAsync(); // Acquire lock for this session
            try
            {
                SessionState sessionState = await GetOrCreateSessionStateAsync(clientAction.SessionId); // Ensures state is loaded
                
                Console.WriteLine($"HUB LOG: SendDrawingAction received for session {clientAction.SessionId}, type {clientAction.Type} from {Context.ConnectionId}. In-memory elements before add: {sessionState.CurrentElements.Count}");

                var element = new DrawingElement
                {
                    SessionId = clientAction.SessionId,
                    ToolType = clientAction.Type,
                    X1 = clientAction.X1, Y1 = clientAction.Y1,
                    X2 = clientAction.X2, Y2 = clientAction.Y2,
                    Color = clientAction.Color, LineWidth = clientAction.LineWidth,
                    StrokeDataJson = clientAction.StrokeDataJson,
                    CreatedAt = DateTime.UtcNow, IsActive = true
                };

                if (clientAction.Type == "circle")
                {
                    double dx = clientAction.X2 - clientAction.X1;
                    double dy = clientAction.Y2 - clientAction.Y1;
                    element.Radius = Math.Sqrt(dx * dx + dy * dy);
                }

                // --- Database Operation ---
                _context.DrawingElements.Add(element);
                var dbStopwatch = Stopwatch.StartNew();
                await _context.SaveChangesAsync(); // Element gets its ID here. This is a critical path for latency.
                dbStopwatch.Stop();
                Console.WriteLine($"HUB LOG: DB Save for new element ID {element.Id} (Session: {clientAction.SessionId}, Type: {element.ToolType}) took {dbStopwatch.ElapsedMilliseconds}ms.");

                // --- Update In-Memory State (after successful DB save) ---
                sessionState.CurrentElements.Add(element); // Add the element with its new DB-generated ID
                sessionState.RedoStack.Clear(); // Any new action clears the redo stack
                
                var actionToBroadcast = new DrawingAction(
                    element.Id, element.SessionId, element.ToolType,
                    element.X1, element.Y1, element.X2, element.Y2,
                    element.Color, element.LineWidth, element.StrokeDataJson, element.Radius
                );

                bool canUndoAfterDraw = sessionState.CurrentElements.Any();
                bool canRedoAfterDraw = sessionState.RedoStack.Any(); // Should be false here

                // --- Broadcast to Clients ---
                var broadcastSw = Stopwatch.StartNew();
                // No need to exclude caller here, client-side de-duplication in Home.razor handles it.
                await Clients.Group(clientAction.SessionId).SendAsync("ReceiveDrawingAction", actionToBroadcast, canUndoAfterDraw, canRedoAfterDraw);
                broadcastSw.Stop();
                
                totalProcessingSw.Stop();
                Console.WriteLine($"HUB LOG: Sent ReceiveDrawingAction (ID {element.Id}) for session {clientAction.SessionId}. Broadcast: {broadcastSw.ElapsedMilliseconds}ms. Total SendDrawingAction processing: {totalProcessingSw.ElapsedMilliseconds}ms. CanUndo: {canUndoAfterDraw}, CanRedo: {canRedoAfterDraw}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HUB ERROR: Exception in SendDrawingAction for session {clientAction.SessionId} by {Context.ConnectionId}: {ex.Message} {ex.StackTrace}");
                // Consider notifying the calling client of the failure.
                // await Clients.Caller.SendAsync("ErrorProcessingAction", "Failed to process your drawing action.");
            }
            finally
            {
                sessionSemaphore.Release(); // Release lock for this session
            }
        }


        public async Task SendCursorPosition(string sessionId, double x, double y)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            // This is a lightweight operation, no DB, no complex state.
            await Clients.OthersInGroup(sessionId).SendAsync("ReceiveCursorPosition", new CursorPosition(Context.ConnectionId, x, y));
        }

        public async Task Undo(string sessionId)
        {
            var totalProcessingSw = Stopwatch.StartNew();
            if (string.IsNullOrEmpty(sessionId)) return;

            var sessionSemaphore = GetSessionSemaphore(sessionId);
            await sessionSemaphore.WaitAsync();
            DrawingElement? undoneElement = null;
            try
            {
                SessionState sessionState = await GetOrCreateSessionStateAsync(sessionId);
                Console.WriteLine($"HUB LOG: Undo request for session {sessionId}. Elements before: {sessionState.CurrentElements.Count}, RedoStack before: {sessionState.RedoStack.Count}.");

                if (sessionState.CurrentElements.Any())
                {
                    undoneElement = sessionState.CurrentElements.Last(); // Get the last active element
                    sessionState.CurrentElements.RemoveAt(sessionState.CurrentElements.Count - 1); // Remove from active list
                    undoneElement.IsActive = false; // Mark as inactive
                    sessionState.RedoStack.Push(undoneElement); // Add to redo stack

                    // --- Database Operation ---
                    _context.DrawingElements.Update(undoneElement); // EF tracks IsActive change
                    var dbStopwatch = Stopwatch.StartNew();
                    await _context.SaveChangesAsync();
                    dbStopwatch.Stop();
                    Console.WriteLine($"HUB LOG: DB Update for Undo (Element ID {undoneElement.Id}, Session: {sessionId}) took {dbStopwatch.ElapsedMilliseconds}ms.");

                    bool currentCanUndo = sessionState.CurrentElements.Any();
                    bool currentCanRedo = sessionState.RedoStack.Any();

                    // --- Broadcast to Clients ---
                    var broadcastSw = Stopwatch.StartNew();
                    await Clients.Group(sessionId).SendAsync("ActionUndone", undoneElement.Id, currentCanUndo, currentCanRedo);
                    broadcastSw.Stop();
                    totalProcessingSw.Stop();
                    Console.WriteLine($"HUB LOG: ActionUndone sent (ID {undoneElement.Id}, Session: {sessionId}). Broadcast: {broadcastSw.ElapsedMilliseconds}ms. Total Undo processing: {totalProcessingSw.ElapsedMilliseconds}ms. CanUndo: {currentCanUndo}, CanRedo: {currentCanRedo}.");
                }
                else
                {
                    totalProcessingSw.Stop();
                    Console.WriteLine($"HUB LOG: Undo requested for session {sessionId} but no elements to undo. Took {totalProcessingSw.ElapsedMilliseconds}ms.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HUB ERROR: Exception in Undo for session {sessionId}: {ex.Message} {ex.StackTrace}");
            }
            finally
            {
                sessionSemaphore.Release();
            }
        }

        public async Task Redo(string sessionId)
        {
            var totalProcessingSw = Stopwatch.StartNew();
            if (string.IsNullOrEmpty(sessionId)) return;
            
            var sessionSemaphore = GetSessionSemaphore(sessionId);
            await sessionSemaphore.WaitAsync();
            DrawingElement? redoneElement = null;
            try
            {
                SessionState sessionState = await GetOrCreateSessionStateAsync(sessionId);
                Console.WriteLine($"HUB LOG: Redo request for session {sessionId}. RedoStack before: {sessionState.RedoStack.Count}, Elements before: {sessionState.CurrentElements.Count}.");

                if (sessionState.RedoStack.Any())
                {
                    redoneElement = sessionState.RedoStack.Pop(); // Get from redo stack
                    redoneElement.IsActive = true; // Mark as active again
                    sessionState.CurrentElements.Add(redoneElement); // Add back to current elements list

                    // --- Database Operation ---
                    _context.DrawingElements.Update(redoneElement); // EF tracks IsActive change
                    var dbStopwatch = Stopwatch.StartNew();
                    await _context.SaveChangesAsync();
                    dbStopwatch.Stop();
                    Console.WriteLine($"HUB LOG: DB Update for Redo (Element ID {redoneElement.Id}, Session: {sessionId}) took {dbStopwatch.ElapsedMilliseconds}ms.");

                    var actionToRedo = new DrawingAction(
                        redoneElement.Id, redoneElement.SessionId, redoneElement.ToolType,
                        redoneElement.X1, redoneElement.Y1, redoneElement.X2, redoneElement.Y2,
                        redoneElement.Color, redoneElement.LineWidth, redoneElement.StrokeDataJson, redoneElement.Radius
                    );

                    bool currentCanUndo = sessionState.CurrentElements.Any();
                    bool currentCanRedo = sessionState.RedoStack.Any();

                    // --- Broadcast to Clients ---
                    var broadcastSw = Stopwatch.StartNew();
                    await Clients.Group(sessionId).SendAsync("ReceiveRedoneAction", actionToRedo, currentCanUndo, currentCanRedo);
                    broadcastSw.Stop();
                    totalProcessingSw.Stop();
                    Console.WriteLine($"HUB LOG: ReceiveRedoneAction sent (ID {redoneElement.Id}, Session: {sessionId}). Broadcast: {broadcastSw.ElapsedMilliseconds}ms. Total Redo processing: {totalProcessingSw.ElapsedMilliseconds}ms. CanUndo: {currentCanUndo}, CanRedo: {currentCanRedo}.");
                }
                else
                {
                    totalProcessingSw.Stop();
                    Console.WriteLine($"HUB LOG: Redo requested for session {sessionId} but RedoStack is empty. Took {totalProcessingSw.ElapsedMilliseconds}ms.");
                }
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"HUB ERROR: Exception in Redo for session {sessionId}: {ex.Message} {ex.StackTrace}");
            }
            finally
            {
                sessionSemaphore.Release();
            }
        }
    }
}
