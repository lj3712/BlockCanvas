using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace BlockCanvas {
    public sealed class Port {
        public Node Owner { get; }
        public PortSide Side { get; }
        public string Name { get; set; }
        public int BitLength { get; set; } = 1; // Number of bits this port expects
        public string? UserTypeName { get; set; } = null; // User-defined type name, null for bit strings

        public RectangleF VisualRect;
        public float ArrowWidth = 12f;
        public float CornerRadius = 6f;
        public SizeF VisualSize = new SizeF(108, 26);
        public PointF ConnectionPoint;
        public float CustomWidth = 108f; // Customizable port width

        public Port(Node owner, PortSide side, string name, int bitLength = 1) { Owner = owner; Side = side; Name = name; BitLength = bitLength; }
        
        // Constructor for user-defined types
        public Port(Node owner, PortSide side, string name, string userTypeName, int bitLength) {
            Owner = owner; Side = side; Name = name; UserTypeName = userTypeName; BitLength = bitLength;
        }
        
        // Helper constructor for backward compatibility during transition
        public Port(Node owner, PortSide side, string name, string typeName) : this(owner, side, name, TypeUtil.GetBitLength(typeName)) { }
        
        // Helper method to get display type
        public string GetDisplayType() => UserTypeName ?? TypeUtil.FormatBitLength(BitLength);
        
        // Helper method to check if this port uses a user-defined type
        public bool IsUserType => !string.IsNullOrEmpty(UserTypeName);


        public RectangleF HitRect {
            get {
                var r = VisualRect;
                if (Side == PortSide.Input) r = RectangleF.FromLTRB(r.Left - ArrowWidth, r.Top, r.Right, r.Bottom);
                if (Side == PortSide.Output) r = RectangleF.FromLTRB(r.Left, r.Top, r.Right + ArrowWidth, r.Bottom);
                return r;
            }
        }
        public bool HitTest(PointF p) {
            if (HitRect.Contains(p)) return true;
            float dx = p.X - ConnectionPoint.X, dy = p.Y - ConnectionPoint.Y;
            return dx * dx + dy * dy <= 100;
        }
    }
}
