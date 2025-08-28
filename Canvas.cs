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
    public sealed partial class Canvas : Panel {
        private Graph root = new();
        private Graph current;
        private RectangleF headerRect = RectangleF.Empty;

        // Selection
        private readonly HashSet<Node> selection = new();
        private bool draggingSelection = false;
        private PointF dragStartMouseWorld;
        private readonly Dictionary<Node, PointF> dragStartPos = new();

        // Wire-dragging
        private Port? connectStartPort;
        private PointF lastMouseWorld;   // mouse in world coords

        // Panning
        private bool panning = false;
        private Point panMouseStart;     // screen coords
        private PointF viewStart;        // starting ViewOffset snapshot

        private enum ResizeHandle { None, N, S, E, W, NE, NW, SE, SW }
        private Node? resizingNode = null;
        private ResizeHandle activeHandle = ResizeHandle.None;
        private PointF resizeStartMouseWorld;
        private PointF resizeStartPos;
        private SizeF resizeStartSize;
        private const float MinNodeW = 140f;
        private const float MinNodeH = 80f;
        private readonly Font titleFont = new Font("Segoe UI", 9f, FontStyle.Bold);


        public Canvas() {
            current = root;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(28, 30, 34);
            ForeColor = Color.White;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseDoubleClick += OnMouseDoubleClick;
            KeyDown += OnKeyDown;
            MouseWheel += OnMouseWheel;
            TabStop = true;
        }

        // Public ops
        public void AddNode(Node n) {
            current.Nodes.Add(n);
            n.LayoutPorts();
            Invalidate();
        }
        public void NewGraph() {
            root = new Graph();
            current = root;
            selection.Clear();
            connectStartPort = null;
            Invalidate();
        }
        public void SaveTo(string path) {
            var dto = new LayoutDto { Version = 2, Graph = ToDto(root) };
            var opts = new JsonSerializerOptions { WriteIndented = true };
            opts.Converters.Add(new PortDefConverter());
            File.WriteAllText(path, JsonSerializer.Serialize(dto, opts));
        }
        public void LoadFrom(string path) {
            var opts = new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
            opts.Converters.Add(new PortDefConverter());
            var dto = JsonSerializer.Deserialize<LayoutDto>(File.ReadAllText(path), opts) ?? throw new InvalidDataException("Invalid file.");
            root = FromDto(dto.Graph, parent: null, owner: null);
            current = root;
            selection.Clear();
            connectStartPort = null;
            Invalidate();
        }

        // ===== World/screen helpers =====
        private PointF ScreenToWorld(PointF p) => new PointF(p.X - current.ViewOffset.X, p.Y - current.ViewOffset.Y);
        private RectangleF VisibleWorldRect() {
            float left = -current.ViewOffset.X;
            float top = -current.ViewOffset.Y;
            return new RectangleF(left, top, ClientSize.Width, ClientSize.Height);
        }
    }
}