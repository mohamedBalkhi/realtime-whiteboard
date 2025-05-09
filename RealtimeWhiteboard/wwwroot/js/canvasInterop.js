// canvasInterop.js
// Includes Ramer-Douglas-Peucker line simplification and optimized logging.

var canvas, ctx;
var cursorCanvas, cursorCtx;
var isDrawing = false;
var dotNetHelperInstance = null;
var currentStrokeStyle = 'black';
var currentLineWidth = 2;
var remoteCursors = {};
var cursorAnimationLoopId = null;
var currentToolJs = 'pen'; // Default tool
var currentPenStrokePoints = []; // Stores points as {x, y} for the current pen stroke
let localDrawingHistory = []; 
let penStrokeInitializedByStartDrawing = false;

// --- Ramer-Douglas-Peucker Line Simplification ---
function perpendicularDistance(point, lineStart, lineEnd) {
    let dx = lineEnd.x - lineStart.x;
    let dy = lineEnd.y - lineStart.y;
    const mag = Math.sqrt(dx * dx + dy * dy);
    if (mag > 0) {
        dx /= mag;
        dy /= mag;
    }
    const pvx = point.x - lineStart.x;
    const pvy = point.y - lineStart.y;
    const pvDot = dx * pvx + dy * pvy;
    const closestPointX = lineStart.x + pvDot * dx;
    const closestPointY = lineStart.y + pvDot * dy;
    const distDx = point.x - closestPointX;
    const distDy = point.y - closestPointY;
    return Math.sqrt(distDx * distDx + distDy * distDy);
}

function ramerDouglasPeucker(points, epsilon) {
    if (!points || points.length < 2) {
        return points;
    }
    let dmax = 0;
    let index = 0;
    const end = points.length - 1;
    for (let i = 1; i < end; i++) {
        const d = perpendicularDistance(points[i], points[0], points[end]);
        if (d > dmax) {
            index = i;
            dmax = d;
        }
    }
    let resultList = [];
    if (dmax > epsilon) {
        const recResults1 = ramerDouglasPeucker(points.slice(0, index + 1), epsilon);
        const recResults2 = ramerDouglasPeucker(points.slice(index, points.length), epsilon);
        resultList = recResults1.slice(0, recResults1.length - 1).concat(recResults2);
    } else {
        resultList = [points[0], points[points.length-1]];
    }
    return resultList;
}
// --- End of RDP ---


export function initializeCanvasAndCursors(mainCanvasElement, overlayCanvasElement, dotNetHelper, width, height, initialColor, initialLineWidth) {
    console.log("JS LOG: initializeCanvasAndCursors attempt.");
    dotNetHelperInstance = dotNetHelper;
    currentStrokeStyle = initialColor || 'black';
    currentLineWidth = initialLineWidth || 2;
    remoteCursors = {};
    if (cursorAnimationLoopId) {
        cancelAnimationFrame(cursorAnimationLoopId);
        cursorAnimationLoopId = null;
    }
    if (canvas && canvas !== mainCanvasElement) {
        // console.log("JS LOG: Removing listeners from old main canvas reference.");
        canvas.removeEventListener('mousedown', startDrawing);
        canvas.removeEventListener('mousemove', draw);
        canvas.removeEventListener('mouseup', stopDrawing);
    }
    canvas = mainCanvasElement;
    cursorCanvas = overlayCanvasElement;
    if (!canvas || !cursorCanvas) {
        console.error("JS ERROR: mainCanvasElement or overlayCanvasElement is null during initialization.");
        return;
    }
    ctx = canvas.getContext('2d');
    cursorCtx = cursorCanvas.getContext('2d');
    if (!ctx || !cursorCtx) {
        console.error("JS ERROR: Failed to get 2D context during initialization.");
        return;
    }
    canvas.removeEventListener('mousedown', startDrawing);
    canvas.removeEventListener('mousemove', draw);
    canvas.removeEventListener('mouseup', stopDrawing);
    canvas.addEventListener('mousedown', startDrawing);
    canvas.addEventListener('mousemove', draw);
    canvas.addEventListener('mouseup', stopDrawing);
    // console.log("JS LOG: Event listeners (re)attached to main canvas.");
    canvas.width = width;
    canvas.height = height;
    cursorCanvas.width = width;
    cursorCanvas.height = height;
    // console.log(`JS LOG: Canvas dimensions set to ${width}x${height}`);
    localDrawingHistory = [];
    // console.log("JS LOG: Local drawing history cleared during canvas initialization.");
    ctx.strokeStyle = currentStrokeStyle;
    ctx.lineWidth = currentLineWidth;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    // console.log(`JS LOG: Drawing settings applied: color=${currentStrokeStyle}, width=${currentLineWidth}`);
    penStrokeInitializedByStartDrawing = false; 
    renderCursorsLoop();
    console.log("JS LOG: initializeCanvasAndCursors completed.");
}

export function setCurrentToolJs(toolName) {
    currentToolJs = toolName;
    console.log(`JS LOG: Tool changed to ${currentToolJs}`);
    if (!isDrawing) { 
        penStrokeInitializedByStartDrawing = false;
    }
}

function renderCursorsLoop() {
    clearCursorCanvas();
    drawRemoteCursors();
    cursorAnimationLoopId = requestAnimationFrame(renderCursorsLoop);
}

export function updateRemoteCursor(connectionId, x, y) {
    remoteCursors[connectionId] = { x: x, y: y, color: getRandomColor(), lastUpdate: Date.now() }; 
}

function drawRemoteCursors() {
    if (!cursorCtx) return;
    Object.values(remoteCursors).forEach(cursor => {
        cursorCtx.beginPath();
        cursorCtx.arc(cursor.x, cursor.y, 5, 0, 2 * Math.PI);
        cursorCtx.fillStyle = cursor.color || 'red';
        cursorCtx.fill();
        cursorCtx.closePath();
    });
}

export function clearCursorCanvas() {
    if (cursorCtx && cursorCanvas) {
        cursorCtx.clearRect(0, 0, cursorCanvas.width, cursorCanvas.height);
    } else {
        // console.error("JS ERROR: clearCursorCanvas - cursorCtx or cursorCanvas is null/undefined.");
    }
}

export function updateDrawingSettings(color, lineWidth) {
    currentStrokeStyle = color;
    currentLineWidth = lineWidth;
    if (ctx) {
        ctx.strokeStyle = currentStrokeStyle;
        ctx.lineWidth = currentLineWidth;
    }
    // console.log(`JS Drawing settings updated: color=${color}, width=${lineWidth}`);
}

export function updateCanvasSize(width, height) {
    if (canvas) {
        canvas.width = width;
        canvas.height = height;
        // console.log(`Both canvases resized to: ${width}x${height}`);
        if (ctx) {
            ctx.strokeStyle = currentStrokeStyle;
            ctx.lineWidth = currentLineWidth;
            ctx.lineCap = 'round';
            ctx.lineJoin = 'round';
        }
    }
    if (cursorCanvas) {
        cursorCanvas.width = width;
        cursorCanvas.height = height;
    }
    penStrokeInitializedByStartDrawing = false; 
}

function startDrawing(e) {
    isDrawing = true;
    penStrokeInitializedByStartDrawing = false; 
    const pos = getMousePos(e);

    if (currentToolJs === 'pen' || currentToolJs === 'eraser') {
        // console.log(`JS PEN: startDrawing - Tool: ${currentToolJs}. Pos: (${pos.x.toFixed(2)}, ${pos.y.toFixed(2)}). Initializing stroke.`);
        ctx.closePath(); 
        ctx.beginPath(); 
        ctx.moveTo(pos.x, pos.y);
        ctx.strokeStyle = currentToolJs === 'eraser' ? '#FFFFFF' : currentStrokeStyle;
        ctx.lineWidth = currentLineWidth;
        currentPenStrokePoints = [{ x: pos.x, y: pos.y }];
        penStrokeInitializedByStartDrawing = true; 
        // console.log(`JS PEN: startDrawing - currentPenStrokePoints initialized. Length: ${currentPenStrokePoints.length}`);
    } else {
        if (dotNetHelperInstance) {
            dotNetHelperInstance.invokeMethodAsync('OnMouseDown', pos.x, pos.y);
        }
    }
}

function draw(e) {
    if (!isDrawing) return;
    const pos = getMousePos(e);
    
    if (dotNetHelperInstance) {
        dotNetHelperInstance.invokeMethodAsync('OnMouseMoveForCursor', pos.x, pos.y);
    }

    if (currentToolJs === 'pen' || currentToolJs === 'eraser') {
        if (!penStrokeInitializedByStartDrawing) {
            console.warn(`JS PEN: draw - Pen tool active, but stroke not initialized by startDrawing. Performing deferred init.`);
            ctx.closePath(); 
            ctx.beginPath(); 
            ctx.moveTo(pos.x, pos.y); 
            ctx.strokeStyle = currentToolJs === 'eraser' ? '#FFFFFF' : currentStrokeStyle;
            ctx.lineWidth = currentLineWidth;
            currentPenStrokePoints = []; 
            penStrokeInitializedByStartDrawing = true; 
        }
        
        currentPenStrokePoints.push({ x: pos.x, y: pos.y });
        
        // This log can be very verbose during drawing, consider removing or making it conditional for debugging.
        // console.log(`JS PEN: draw - Tool: ${currentToolJs}. Added point. Points in stroke: ${currentPenStrokePoints.length}.`);
        
        ctx.lineTo(pos.x, pos.y);
        ctx.stroke(); 
    } 
}

function stopDrawing(e) {
    if (!isDrawing) return;
    isDrawing = false;
    penStrokeInitializedByStartDrawing = false; 
    const pos = getMousePos(e); 

    if (currentToolJs === 'pen' || currentToolJs === 'eraser') {
        // console.log(`JS PEN: stopDrawing - Tool: ${currentToolJs}. Original points: ${currentPenStrokePoints.length}.`);
        
        let pointsToSend = currentPenStrokePoints;
        if (pointsToSend.length > 2) {
            // Epsilon is the tolerance. Smaller values mean less simplification.
            // Adjust this value based on desired smoothness vs. performance.
            // Good starting values might be between 0.5 and 2.0.
            const epsilon = 1.0; // TUNABLE PARAMETER
            const simplifiedPoints = ramerDouglasPeucker(pointsToSend, epsilon);
            console.log(`JS PEN: RDP simplified points from ${pointsToSend.length} to ${simplifiedPoints.length} with epsilon ${epsilon}.`);
            pointsToSend = simplifiedPoints;
        }

        const pointsForDotNet = pointsToSend.map(p => ({ X: p.x, Y: p.y }));

        if (pointsForDotNet.length > 0) { 
            const strokeType = currentToolJs === 'eraser' ? 'OnEraserStrokeCompleted' : 'OnPenStrokeCompleted';
            if (dotNetHelperInstance) {
                 dotNetHelperInstance.invokeMethodAsync(strokeType, pointsForDotNet);
            }
        }
        currentPenStrokePoints = []; 
        // console.log("JS PEN: stopDrawing - currentPenStrokePoints cleared.");
    } else {
        if (dotNetHelperInstance) {
            dotNetHelperInstance.invokeMethodAsync('OnMouseUp', pos.x, pos.y);
        }
    }
}

function getMousePos(evt) {
    const rect = canvas.getBoundingClientRect();
    return {
        x: evt.clientX - rect.left,
        y: evt.clientY - rect.top
    };
}

export function processInitialHistory(actionsFromServer) {
    if (!Array.isArray(actionsFromServer)) {
        console.warn("JS WARN: processInitialHistory received non-array data:", actionsFromServer);
        localDrawingHistory = [];
    } else {
        // console.log(`JS LOG: processInitialHistory - Received ${actionsFromServer.length} actions.`);
        localDrawingHistory = JSON.parse(JSON.stringify(actionsFromServer)); 
    }
    // const redrawStart = performance.now();
    redrawAllFromLocalHistory();
    // const redrawEnd = performance.now();
    // console.log(`JS LOG: processInitialHistory - redrawAllFromLocalHistory() took ${(redrawEnd - redrawStart).toFixed(2)}ms for ${localDrawingHistory.length} actions.`);
    penStrokeInitializedByStartDrawing = false; 
}

function redrawAllFromLocalHistory() {
    clearCanvas(); 
    if (!ctx || !canvas) {
        console.error("JS ERROR: redrawAllFromLocalHistory - Canvas context not available.");
        return;
    }
    // console.log(`JS LOG: redrawAllFromLocalHistory - Starting to redraw ${localDrawingHistory.length} actions.`);
    // const overallRedrawStart = performance.now();

    localDrawingHistory.forEach((action) => { // Removed index for slight perf, not used
        if (action && typeof action.type !== 'undefined') { 
            const originalStrokeStyle = ctx.strokeStyle;
            const originalLineWidth = ctx.lineWidth;
            // lineCap and lineJoin are consistently 'round'
            
            ctx.strokeStyle = action.color || currentStrokeStyle; 
            ctx.lineWidth = action.lineWidth || currentLineWidth; 

            executeDrawCommandForAction(action); 

            ctx.strokeStyle = originalStrokeStyle; 
            ctx.lineWidth = originalLineWidth;
        } else {
            // console.warn("JS WARN: redrawAllFromLocalHistory encountered invalid action:", action);
        }
    });
    // const overallRedrawEnd = performance.now();
    // console.log(`JS LOG: redrawAllFromLocalHistory - COMPLETED redrawing. Total time: ${(overallRedrawEnd - overallRedrawStart).toFixed(2)}ms.`);
}

function executeDrawCommandForAction(action) {
    if (!ctx || !action) return; // Simplified error check

    ctx.beginPath(); 
    switch (action.type) {
        case 'line':
            ctx.moveTo(action.x1, action.y1);
            ctx.lineTo(action.x2, action.y2);
            ctx.stroke();
            break;
        case 'rectangle':
            ctx.rect(action.x1 < action.x2 ? action.x1 : action.x2, 
                     action.y1 < action.y2 ? action.y1 : action.y2, 
                     Math.abs(action.x2 - action.x1), Math.abs(action.y2 - action.y1));
            ctx.stroke();
            break;
        case 'circle':
            let radius = action.radius; 
            if (typeof radius !== 'number' || radius <= 0) {
                radius = Math.sqrt(Math.pow(action.x2 - action.x1, 2) + Math.pow(action.y2 - action.y1, 2));
            }
            if (radius > 0) {
                ctx.arc(action.x1, action.y1, radius, 0, 2 * Math.PI);
                ctx.stroke();
            }
            break;
        case 'pen-stroke': 
        case 'eraser-stroke': 
            if (action.strokeDataJson) {
                let points;
                try {
                    points = JSON.parse(action.strokeDataJson); 
                } catch (e) {
                    console.error("JS ERROR: pen-stroke/eraser-stroke JSON.parse error:", e);
                    return;
                }
                if (points && points.length > 0) {
                    ctx.moveTo(points[0].X, points[0].Y); 
                    for (let i = 1; i < points.length; i++) {
                        ctx.lineTo(points[i].X, points[i].Y); 
                    }
                    ctx.stroke(); 
                }
            }
            break;
        // default: console.warn("Unknown action type in executeDrawCommandForAction:", action.type); // Can be noisy
    }
}

export function undoActionById(actionId) {
    const initialLength = localDrawingHistory.length;
    localDrawingHistory = localDrawingHistory.filter(action => action.id !== actionId);
    if (localDrawingHistory.length < initialLength) {
        redrawAllFromLocalHistory(); 
        console.log(`JS LOG: Action ${actionId} undone. Redrew ${localDrawingHistory.length} actions.`);
    } else {
        console.warn(`JS WARN: Action ${actionId} not found in local history for undo.`);
    }
    penStrokeInitializedByStartDrawing = false; 
}

export function drawRemoteAction(action) {
    if (!ctx || !canvas) {
        console.error("JS ERROR: Canvas context not available for drawRemoteAction");
        return;
    }

    if (action && typeof action.id !== 'undefined') {
        const existingActionIndex = localDrawingHistory.findIndex(a => a.id === action.id);
        if (existingActionIndex > -1) {
            localDrawingHistory[existingActionIndex] = JSON.parse(JSON.stringify(action));
            // console.log(`JS LOG: Updated action ${action.id} in local history (e.g. redo). Redrawing all.`);
            redrawAllFromLocalHistory(); 
        } else {
            localDrawingHistory.push(JSON.parse(JSON.stringify(action))); 
            const originalStrokeStyle = ctx.strokeStyle;
            const originalLineWidth = ctx.lineWidth;
            ctx.strokeStyle = action.color || currentStrokeStyle;
            ctx.lineWidth = action.lineWidth || currentLineWidth;
            // lineCap and lineJoin are 'round' by default from initialize
            executeDrawCommandForAction(action); 
            ctx.strokeStyle = originalStrokeStyle; 
            ctx.lineWidth = originalLineWidth;
            // console.log(`JS LOG: Drew and added new action ${action.id} to local history. Total: ${localDrawingHistory.length}`);
        }
    } else {
        console.warn("JS WARN: drawRemoteAction received action without an ID or invalid action:", action);
        // Fallback for old-format or unexpected actions
        const originalStrokeStyle = ctx.strokeStyle;
        const originalLineWidth = ctx.lineWidth;
        ctx.strokeStyle = action.color || currentStrokeStyle;
        ctx.lineWidth = action.lineWidth || currentLineWidth;
        executeDrawCommandForAction(action); 
        ctx.strokeStyle = originalStrokeStyle;
        ctx.lineWidth = originalLineWidth;
    }
    penStrokeInitializedByStartDrawing = false; 
}

export function clearCanvas() {
    if (ctx && canvas) {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        // console.log("JS LOG: Canvas cleared via clearRect.");
    }
    penStrokeInitializedByStartDrawing = false; 
}

function getRandomColor() {
    const letters = '0123456789ABCDEF';
    let color = '#';
    for (let i = 0; i < 6; i++) {
        color += letters[Math.floor(Math.random() * 16)];
    }
    return color;
}

// JSTest_ForceRepaint is likely not needed for general operation.
// export function JSTest_ForceRepaint() { ... }
