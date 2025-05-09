using System.ComponentModel.DataAnnotations;

namespace RealtimeWhiteboard.Models
{
    public class DrawingElement
    {
        public int Id { get; set; }

        [Required]
        public string SessionId { get; set; } = "default_session"; // Default session for now

        [Required]
        public string ToolType { get; set; } = string.Empty; // Renamed from Type. e.g., "pen", "eraser", "line", "rectangle", "circle"

        // Coordinates used for lines, pen/eraser segments, rectangle corners.
        // For circles, (X1, Y1) is the center.
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }

        // Specific property for circles
        public double? Radius { get; set; }

        public string? Color { get; set; }
        public double? LineWidth { get; set; }

        // Could add Timestamp, UserId, etc. later
        public string? StrokeDataJson { get; set; } // For path-based tools like pen, eraser
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true; // Used for soft-deleting elements during undo
    }
} 