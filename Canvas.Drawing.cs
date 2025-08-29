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
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // 1) Header (no transform)
            DrawBreadcrumbBar(g);

            // 2) World translation for everything else (grid + nodes + wires)
            g.TranslateTransform(current.ViewOffset.X, current.ViewOffset.Y);

            DrawGrid(g);          // drawn in world coords
            LayoutGraph(g);       // computes proxies etc. (world coords overall)
            foreach (var edge in current.Edges) DrawEdge(g, edge);
            foreach (var n in current.Nodes) DrawNode(g, n);

            if (connectStartPort != null) {
                using var pen = new Pen(Color.FromArgb(180, 120, 200, 255), 2) { DashStyle = DashStyle.Dot };
                DrawCurve(g, connectStartPort.ConnectionPoint, lastMouseWorld, pen);
            }
        }

        private void LayoutGraph(Graphics g) {

            foreach (var n in current.Nodes) {
                if (!n.IsProxy) {
                    UpdateTitleHeight(g, n); // NEW: compute dynamic title bar height
                    n.LayoutPorts();
                }
            }
            if (current.Owner != null) EnsureProxiesMatch(current.Owner);

            // Place proxies in world coords near nominal "viewport" edges (using current.ViewOffset)
            const float sideMargin = 16f;
            var vis = VisibleWorldRect();
            var area = InnerWorkingRect(); // already in your file

            var inlets = current.Nodes.Where(n => n.IsProxy && n.ProxyIsInlet).OrderBy(n => n.ProxyIndex).ToList();
            var outlets = current.Nodes.Where(n => n.IsProxy && !n.ProxyIsInlet).OrderBy(n => n.ProxyIndex).ToList();

            float YAtSlot(int i, int n) {
                if (n <= 1) return area.Top + area.Height * 0.5f;
                float t = (i + 0.5f) / n;
                return area.Top + t * area.Height;
            }

            // Inlets (left)
            for (int i = 0; i < inlets.Count; i++) {
                var pn = inlets[i];
                var p = pn.Outputs[0];
                float y = YAtSlot(i, inlets.Count);
                var rect = new RectangleF(vis.Left + sideMargin, y - p.VisualSize.Height / 2f, p.VisualSize.Width, p.VisualSize.Height);
                p.VisualRect = rect;
                p.ConnectionPoint = new PointF(Math.Max(vis.Left + 4, rect.Left - p.ArrowWidth), y);
            }

            // Outlets (right)
            for (int i = 0; i < outlets.Count; i++) {
                var pn = outlets[i];
                var p = pn.Inputs[0];
                float y = YAtSlot(i, outlets.Count);
                var rect = new RectangleF(vis.Right - sideMargin - p.VisualSize.Width, y - p.VisualSize.Height / 2f, p.VisualSize.Width, p.VisualSize.Height);
                p.VisualRect = rect;
                p.ConnectionPoint = new PointF(Math.Min(vis.Right - 4, rect.Right + p.ArrowWidth), y);
            }

        }

        private void DrawBreadcrumbBar(Graphics g) {
            float barH = 28f;
            var rect = new RectangleF(0, 0, ClientSize.Width, barH);
            headerRect = rect;

            using var bg = new SolidBrush(Color.FromArgb(60, 70, 82));
            using var border = new Pen(Color.FromArgb(90, 95, 105), 1);
            g.FillRectangle(bg, rect);
            g.DrawLine(border, rect.Left, rect.Bottom - 1, rect.Right, rect.Bottom - 1);

            var trail = GetTrail();
            int level = trail.Count;

            string text = "Level " + level + " — root";
            foreach (var n in trail) text += " ▸ " + n.Title;

            using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var tb = new SolidBrush(Color.White);
            g.DrawString(text, font, tb, new PointF(8, 6));

            if (level > 0) {
                using var f2 = new Font("Segoe UI", 8f, FontStyle.Regular);
                using var tb2 = new SolidBrush(Color.Gainsboro);
                const string tip = "Double-click to zoom out";
                var size = g.MeasureString(tip, f2);
                g.DrawString(tip, f2, tb2, new PointF(ClientSize.Width - size.Width - 8, 6));
            }
        }

        private void DrawGrid(Graphics g) {
            // Draw an "infinite" grid within the visible world rect
            var vis = VisibleWorldRect();
            int s1 = 20, s2 = 100;

            float startX1 = (float)Math.Floor(vis.Left / s1) * s1;
            float startY1 = (float)Math.Floor(vis.Top / s1) * s1;
            float startX2 = (float)Math.Floor(vis.Left / s2) * s2;
            float startY2 = (float)Math.Floor(vis.Top / s2) * s2;

            using var p1 = new Pen(Color.FromArgb(40, 40, 45), 1);
            using var p2 = new Pen(Color.FromArgb(48, 50, 56), 1);

            for (float x = startX1; x <= vis.Right; x += s1) g.DrawLine(p1, x, vis.Top, x, vis.Bottom);
            for (float y = startY1; y <= vis.Bottom; y += s1) g.DrawLine(p1, vis.Left, y, vis.Right, y);
            for (float x = startX2; x <= vis.Right; x += s2) g.DrawLine(p2, x, vis.Top, x, vis.Bottom);
            for (float y = startY2; y <= vis.Bottom; y += s2) g.DrawLine(p2, vis.Left, y, vis.Right, y);
        }

        private void DrawNode(Graphics g, Node n) {
            if (n.IsProxy) {
                if (n.ProxyIsInlet) DrawPort(g, n.Outputs[0], isInput: true);
                else DrawPort(g, n.Inputs[0], isInput: false);
                return;
            }

            var rect = n.Bounds;
            var selected = selection.Contains(n);

            // Special diamond shape for Decision blocks
            if (n.Type == NodeType.Decision) {
                DrawDecisionNode(g, n, selected);
                return;
            }
            
            // Special thin bar for Null Consumer blocks
            if (n.Type == NodeType.NullConsumer) {
                DrawNullConsumerNode(g, n, selected);
                return;
            }

            float radius = 10f;
            using var path = RoundedRect(rect, radius);
            
            // Special colors for START and END blocks
            LinearGradientBrush fill;
            Pen border;
            
            switch (n.Type) {
                case NodeType.Start:
                    fill = new LinearGradientBrush(rect, Color.FromArgb(58, 120, 58), Color.FromArgb(44, 80, 44), LinearGradientMode.Vertical);
                    border = new Pen(selected ? Color.FromArgb(120, 255, 120) : Color.FromArgb(85, 150, 85), selected ? 2.5f : 1.5f);
                    break;
                case NodeType.End:
                    fill = new LinearGradientBrush(rect, Color.FromArgb(120, 58, 58), Color.FromArgb(80, 44, 44), LinearGradientMode.Vertical);
                    border = new Pen(selected ? Color.FromArgb(255, 120, 120) : Color.FromArgb(150, 85, 85), selected ? 2.5f : 1.5f);
                    break;
                case NodeType.Const:
                    fill = new LinearGradientBrush(rect, Color.FromArgb(120, 120, 58), Color.FromArgb(80, 80, 44), LinearGradientMode.Vertical);
                    border = new Pen(selected ? Color.FromArgb(255, 255, 120) : Color.FromArgb(150, 150, 85), selected ? 2.5f : 1.5f);
                    break;
                case NodeType.Nand:
                    fill = new LinearGradientBrush(rect, Color.FromArgb(80, 58, 120), Color.FromArgb(60, 44, 80), LinearGradientMode.Vertical);
                    border = new Pen(selected ? Color.FromArgb(160, 120, 255) : Color.FromArgb(120, 85, 150), selected ? 2.5f : 1.5f);
                    break;
                case NodeType.Marshaller:
                    fill = new LinearGradientBrush(rect, Color.FromArgb(120, 80, 58), Color.FromArgb(80, 60, 44), LinearGradientMode.Vertical);
                    border = new Pen(selected ? Color.FromArgb(255, 160, 120) : Color.FromArgb(150, 120, 85), selected ? 2.5f : 1.5f);
                    break;
                default:
                    fill = new LinearGradientBrush(rect, Color.FromArgb(58, 62, 70), Color.FromArgb(44, 47, 53), LinearGradientMode.Vertical);
                    border = new Pen(selected ? Color.FromArgb(120, 190, 255) : Color.FromArgb(85, 90, 100), selected ? 2.5f : 1.5f);
                    break;
            }

            g.FillPath(fill, path);
            g.DrawPath(border, path);
            fill.Dispose();
            border.Dispose();

            // Title bar
            var titleRect = new RectangleF(rect.X, rect.Y, rect.Width, n.TitleH);
            SolidBrush titleBrush;
            Pen titleBorder;
            
            switch (n.Type) {
                case NodeType.Start:
                    titleBrush = new SolidBrush(Color.FromArgb(70, 120, 70));
                    titleBorder = new Pen(Color.FromArgb(90, 150, 90), 1);
                    break;
                case NodeType.End:
                    titleBrush = new SolidBrush(Color.FromArgb(120, 70, 70));
                    titleBorder = new Pen(Color.FromArgb(150, 90, 90), 1);
                    break;
                case NodeType.Const:
                    titleBrush = new SolidBrush(Color.FromArgb(120, 120, 70));
                    titleBorder = new Pen(Color.FromArgb(150, 150, 90), 1);
                    break;
                case NodeType.Nand:
                    titleBrush = new SolidBrush(Color.FromArgb(90, 70, 120));
                    titleBorder = new Pen(Color.FromArgb(120, 90, 150), 1);
                    break;
                case NodeType.Marshaller:
                    titleBrush = new SolidBrush(Color.FromArgb(120, 90, 70));
                    titleBorder = new Pen(Color.FromArgb(150, 120, 90), 1);
                    break;
                default:
                    titleBrush = new SolidBrush(Color.FromArgb(70, 74, 82));
                    titleBorder = new Pen(Color.FromArgb(90, 95, 105), 1);
                    break;
            }
            
            g.FillRectangle(titleBrush, titleRect);
            g.DrawLine(titleBorder, titleRect.Left, titleRect.Bottom, titleRect.Right, titleRect.Bottom);
            titleBrush.Dispose();
            titleBorder.Dispose();

            using var textBrush = new SolidBrush(Color.White);
            var sfTitle = new StringFormat {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.LineLimit
            };
            
            // For CONST blocks, show the value in addition to the title
            string displayTitle = n.Type == NodeType.Const ? $"{n.Title} [{n.ConstValue}]" : n.Title;
            g.DrawString(displayTitle, titleFont, textBrush,
                         new RectangleF(titleRect.X + 8, titleRect.Y + 6, titleRect.Width - 16, titleRect.Height - 8),
                         sfTitle);
            foreach (var p in n.Inputs) DrawPort(g, p, isInput: true);
            foreach (var p in n.Outputs) DrawPort(g, p, isInput: false);

            if (selection.Contains(n)) {
                DrawResizeHandles(g, n);
                DrawPortResizeHandles(g, n);
            }
        }

        private void DrawPort(Graphics g, Port port, bool isInput) {
            float r = port.CornerRadius;
            var rect = port.VisualRect;

            var baseC = TypeUtil.GetPortColor(port);
            Color cTop = ControlPaint.Light(baseC);
            Color cBot = ControlPaint.Dark(baseC);

            using var path = RoundedRect(rect, r);
            using var fill = new LinearGradientBrush(rect, cTop, cBot, LinearGradientMode.Vertical);
            using var border = new Pen(Color.FromArgb(25, 25, 25), 1.25f);

            g.FillPath(fill, path);
            g.DrawPath(border, path);

            // Chevron
            var midY = rect.Top + rect.Height / 2f;
            PointF a, b, c;
            if (isInput) {
                a = new PointF(rect.Left, midY - rect.Height * 0.30f);
                b = new PointF(rect.Left, midY + rect.Height * 0.30f);
                c = new PointF(rect.Left - port.ArrowWidth, midY);
            }
            else {
                a = new PointF(rect.Right, midY - rect.Height * 0.30f);
                b = new PointF(rect.Right, midY + rect.Height * 0.30f);
                c = new PointF(rect.Right + port.ArrowWidth, midY);
            }
            using (var chev = new GraphicsPath()) {
                chev.AddPolygon(new[] { a, b, c });
                using var chevBrush = new SolidBrush(ControlPaint.LightLight(baseC));
                using var chevBorder = new Pen(Color.FromArgb(25, 25, 25), 1.0f);
                g.FillPath(chevBrush, chev);
                g.DrawPath(chevBorder, chev);
            }

            port.ConnectionPoint = isInput
                ? new PointF(rect.Left - port.ArrowWidth, midY)
                : new PointF(rect.Right + port.ArrowWidth, midY);

            using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            using var tb = new SolidBrush(Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            g.DrawString($"{port.Name} : {port.GetDisplayType()}", font, tb, rect, sf);
        }

        private void DrawEdge(Graphics g, Edge e) {
            bool isSelected = selectedWire == e;
            var color = isSelected ? Color.FromArgb(255, 220, 120) : Color.FromArgb(170, 170, 190);
            var width = isSelected ? 3f : 2f;
            using var pen = new Pen(color, width);
            DrawCurve(g, e.FromPort.ConnectionPoint, e.ToPort.ConnectionPoint, pen);
        }

        private void DrawCurve(Graphics g, PointF a, PointF b, Pen p) {
            float dx = Math.Abs(b.X - a.X) * 0.5f + 24f;
            var c1 = new PointF(a.X + dx, a.Y);
            var c2 = new PointF(b.X - dx, b.Y);
            using var path = new GraphicsPath();
            path.AddBezier(a, c1, c2, b);
            g.DrawPath(p, path);
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius) {
            var path = new GraphicsPath();
            if (radius <= 0f) { path.AddRectangle(rect); path.CloseFigure(); return path; }

            float d = radius * 2f;
            var tl = new RectangleF(rect.Left, rect.Top, d, d);
            var tr = new RectangleF(rect.Right - d, rect.Top, d, d);
            var br = new RectangleF(rect.Right - d, rect.Bottom - d, d, d);
            var bl = new RectangleF(rect.Left, rect.Bottom - d, d, d);

            path.AddArc(tl, 180, 90);
            path.AddArc(tr, 270, 90);
            path.AddArc(br, 0, 90);
            path.AddArc(bl, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void DrawResizeHandles(Graphics g, Node n) {
            if (n.IsProxy) return;
            using var br = new SolidBrush(Color.White);
            using var pen = new Pen(Color.Black, 1f);
            foreach (var rect in GetHandleRects(n).Values) {
                g.FillRectangle(br, rect);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }

        private void DrawPortResizeHandles(Graphics g, Node n) {
            if (n.IsProxy) return;
            using var br = new SolidBrush(Color.LightBlue);
            using var pen = new Pen(Color.Blue, 1f);
            const float handleWidth = 6f;
            
            foreach (var port in n.Inputs.Concat(n.Outputs)) {
                var rect = port.VisualRect;
                RectangleF handle;
                
                if (port.Side == PortSide.Input) {
                    // Left edge handle for input ports
                    handle = new RectangleF(rect.Left - handleWidth/2, rect.Top + 2, handleWidth, rect.Height - 4);
                } else {
                    // Right edge handle for output ports
                    handle = new RectangleF(rect.Right - handleWidth/2, rect.Top + 2, handleWidth, rect.Height - 4);
                }
                
                g.FillRectangle(br, handle);
                g.DrawRectangle(pen, handle.X, handle.Y, handle.Width, handle.Height);
            }
        }

        private void DrawDecisionNode(Graphics g, Node n, bool selected) {
            var rect = n.Bounds;
            var centerX = rect.X + rect.Width / 2;
            var centerY = rect.Y + rect.Height / 2;
            
            // Create diamond shape
            var diamond = new PointF[] {
                new PointF(centerX, rect.Top),           // Top
                new PointF(rect.Right, centerY),        // Right
                new PointF(centerX, rect.Bottom),       // Bottom
                new PointF(rect.Left, centerY)          // Left
            };
            
            // Fill and border colors - purple/magenta for decision blocks
            var fillColor1 = Color.FromArgb(120, 58, 120);
            var fillColor2 = Color.FromArgb(80, 44, 80);
            var borderColor = selected ? Color.FromArgb(255, 120, 255) : Color.FromArgb(150, 85, 150);
            
            using var path = new GraphicsPath();
            path.AddPolygon(diamond);
            
            using var fill = new LinearGradientBrush(rect, fillColor1, fillColor2, LinearGradientMode.Vertical);
            using var border = new Pen(borderColor, selected ? 2.5f : 1.5f);
            
            g.FillPath(fill, path);
            g.DrawPath(border, path);
            
            // Draw title text
            using var textBrush = new SolidBrush(Color.White);
            var sf = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };
            
            g.DrawString(n.Title, titleFont, textBrush, rect, sf);
            
            // Draw ports
            foreach (var p in n.Inputs) DrawPort(g, p, isInput: true);
            foreach (var p in n.Outputs) DrawPort(g, p, isInput: false);
            
            if (selected) {
                DrawPortResizeHandles(g, n);
                // Note: No regular resize handles for diamond shape
            }
        }

        private void DrawNullConsumerNode(Graphics g, Node n, bool selected) {
            var rect = n.Bounds;
            
            // Colors for null consumer - dark gray/black theme
            var fillColor1 = Color.FromArgb(40, 40, 40);
            var fillColor2 = Color.FromArgb(20, 20, 20);
            var borderColor = selected ? Color.FromArgb(160, 160, 160) : Color.FromArgb(80, 80, 80);
            
            // Simple rectangle with minimal rounding
            float radius = 3f;
            using var path = RoundedRect(rect, radius);
            
            using var fill = new LinearGradientBrush(rect, fillColor1, fillColor2, LinearGradientMode.Vertical);
            using var border = new Pen(borderColor, selected ? 2.5f : 1.5f);
            
            g.FillPath(fill, path);
            g.DrawPath(border, path);
            
            // Draw title text in smaller font
            using var textBrush = new SolidBrush(Color.LightGray);
            var sf = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };
            
            // Use smaller font for thin bar
            using var smallFont = new Font(titleFont.FontFamily, titleFont.Size * 0.8f, titleFont.Style);
            g.DrawString(n.Title, smallFont, textBrush, rect, sf);
            
            // Draw ports
            foreach (var p in n.Inputs) DrawPort(g, p, isInput: true);
            // No outputs to draw
            
            if (selected) {
                DrawPortResizeHandles(g, n);
                DrawResizeHandles(g, n);
            }
        }

        // Recompute a node's TitleH based on its width, wrapping (max 3 lines) with ellipsis
        private void UpdateTitleHeight(Graphics g, Node n) {
            const float minH = 28f;
            const int maxLines = 3;
            float availW = Math.Max(1f, n.Size.Width - 16f); // padding from left/right
            using var sf = new StringFormat {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.LineLimit
            };

            // Measure wrapped height
            var measured = g.MeasureString(n.Title ?? "", titleFont, new SizeF(availW, float.MaxValue), sf);
            float lineH = titleFont.GetHeight(g);
            float capped = Math.Min(measured.Height, maxLines * lineH);
            n.TitleH = Math.Max(minH, (float)Math.Ceiling(capped));
        }
    }
}