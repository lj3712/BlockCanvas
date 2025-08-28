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
        private void GroupSelectionIntoBlock() {
            var sel = selection.Where(n => !n.IsProxy).Distinct().ToList();
            if (sel.Count == 0) { System.Media.SystemSounds.Beep.Play(); return; }

            string defaultName = sel.Count == 1 ? sel[0].Title + "_grp" : "Group";
            string name = Microsoft.VisualBasic.Interaction.InputBox("New block name:", "Group Selected into Block", defaultName);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();

            // Selection bounds in WORLD space
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var n in sel) {
                minX = Math.Min(minX, n.Position.X);
                minY = Math.Min(minY, n.Position.Y);
                maxX = Math.Max(maxX, n.Position.X + n.Size.Width);
                maxY = Math.Max(maxY, n.Position.Y + n.Size.Height);
            }
            var groupRect = RectangleF.FromLTRB(minX, minY, maxX, maxY);

            var selSet = new HashSet<Node>(sel);
            var edgesWithin = current.Edges.Where(ed => selSet.Contains(ed.FromNode) && selSet.Contains(ed.ToNode)).ToList();
            var incoming = current.Edges.Where(ed => !selSet.Contains(ed.FromNode) && selSet.Contains(ed.ToNode)).ToList();
            var outgoing = current.Edges.Where(ed => selSet.Contains(ed.FromNode) && !selSet.Contains(ed.ToNode)).ToList();

            bool HasIncomingFromOutside(Port input) => current.Edges.Any(ed => ed.ToPort == input && !selSet.Contains(ed.FromNode));
            bool HasOutgoingToOutside(Port output) => current.Edges.Any(ed => ed.FromPort == output && !selSet.Contains(ed.ToNode));

            static List<Port> DistinctPortsByRef(IEnumerable<Port> ports) {
                var set = new HashSet<Port>();
                var list = new List<Port>();
                foreach (var p in ports) if (set.Add(p)) list.Add(p);
                return list;
            }

            var boundaryInputs = DistinctPortsByRef(sel.SelectMany(n => n.Inputs.Where(HasIncomingFromOutside)))
                                  .OrderBy(p => p.Owner.Position.Y).ToList();
            var boundaryOutputs = DistinctPortsByRef(sel.SelectMany(n => n.Outputs.Where(HasOutgoingToOutside)))
                                  .OrderBy(p => p.Owner.Position.Y).ToList();

            // Create composite node (can be zero-port)
            var grp = new Node(name, new PointF(groupRect.X - 10, groupRect.Y - 10), createDefaultPorts: false) {
                Inner = new Graph { Parent = current, Owner = null }
            };
            grp.Inner!.Owner = grp;

            // Group ports + maps
            var mapIn = new Dictionary<Port, Port>();
            var mapOut = new Dictionary<Port, Port>();

            foreach (var p in boundaryInputs) {
                string proposed = p.Owner.Title + "." + p.Name;
                var gPort = new Port(grp, PortSide.Input, UniquePortName(grp.Inputs.Select(pp => pp.Name), proposed), p.TypeName);
                grp.Inputs.Add(gPort);
                mapIn[p] = gPort;
            }
            foreach (var p in boundaryOutputs) {
                string proposed = p.Owner.Title + "." + p.Name;
                var gPort = new Port(grp, PortSide.Output, UniquePortName(grp.Outputs.Select(pp => pp.Name), proposed), p.TypeName);
                grp.Outputs.Add(gPort);
                mapOut[p] = gPort;
            }

            AutoSizeForPorts(grp, allowShrink: false);
            current.Nodes.Add(grp);
            grp.LayoutPorts();

            foreach (var n in sel) { current.Nodes.Remove(n); grp.Inner!.Nodes.Add(n); }

            foreach (var ed in edgesWithin) current.Edges.Remove(ed);
            foreach (var ed in incoming) current.Edges.Remove(ed);
            foreach (var ed in outgoing) current.Edges.Remove(ed);

            EnsureProxiesMatch(grp);

            Port InletProxyOut(int idx) {
                var pn = grp.Inner!.Nodes.First(n => n.IsProxy && n.ProxyIsInlet && n.ProxyIndex == idx);
                return pn.Outputs[0];
            }
            Port OutletProxyIn(int idx) {
                var pn = grp.Inner!.Nodes.First(n => n.IsProxy && !n.ProxyIsInlet && n.ProxyIndex == idx);
                return pn.Inputs[0];
            }

            foreach (var ed in edgesWithin) grp.Inner!.Edges.Add(ed);

            foreach (var p in boundaryInputs) {
                var grpIn = mapIn[p];
                int idx = grp.Inputs.IndexOf(grpIn);
                foreach (var ed in incoming.Where(ed => ed.ToPort == p))
                    current.Edges.Add(new Edge(ed.FromPort, grpIn));
                grp.Inner!.Edges.Add(new Edge(InletProxyOut(idx), p));
            }
            foreach (var p in boundaryOutputs) {
                var grpOut = mapOut[p];
                int idx = grp.Outputs.IndexOf(grpOut);
                grp.Inner!.Edges.Add(new Edge(p, OutletProxyIn(idx)));
                foreach (var ed in outgoing.Where(ed => ed.FromPort == p))
                    current.Edges.Add(new Edge(grpOut, ed.ToPort));
            }

            PlaceNodesIntoInnerArea(sel);

            selection.Clear();
            selection.Add(grp);
            Invalidate();
        }

        private void ZoomInto(Node node) {
            if (node.Inner == null)
                node.Inner = new Graph { Parent = current, Owner = node };

            EnsureProxiesMatch(node);
            current = node.Inner!;
            selection.Clear();
            Invalidate();
        }

        private void ZoomOut() {
            if (current.Parent != null) {
                current = current.Parent;
                selection.Clear();
                Invalidate();
            }
        }

        private Port? FindPortAt(PointF worldP) {
            for (int i = current.Nodes.Count - 1; i >= 0; i--) {
                var n = current.Nodes[i];
                foreach (var port in n.Inputs.Concat(n.Outputs))
                    if (port.HitTest(worldP)) return port;
            }
            return null;
        }
        private Edge? FindEdge(Port from, Port to) => current.Edges.FirstOrDefault(ed => ed.FromPort == from && ed.ToPort == to);

        private List<Node> GetTrail() {
            var trail = new List<Node>();
            var g = current;
            while (g.Owner != null) { trail.Add(g.Owner); g = g.Parent!; }
            trail.Reverse();
            return trail;
        }
        private int CurrentLevel() => GetTrail().Count;

        private void AddInputPort(Node node, string? name = null, string typeName = "Integer") {
            name ??= UniquePortName(node.Inputs.Select(p => p.Name), "In");
            node.Inputs.Add(new Port(node, PortSide.Input, name, typeName));
            AutoSizeForPorts(node, allowShrink: false);
            EnsureProxiesMatch(node);
            Invalidate();
        }
        private void AddOutputPort(Node node, string? name = null, string typeName = "Integer") {
            name ??= UniquePortName(node.Outputs.Select(p => p.Name), "Out");
            node.Outputs.Add(new Port(node, PortSide.Output, name, typeName));
            AutoSizeForPorts(node, allowShrink: false);
            EnsureProxiesMatch(node);
            Invalidate();
        }


        private void DeletePort(Port port) {
            if (port.Owner.IsProxy) return;
            var node = port.Owner;
            current.Edges.RemoveAll(ed => ed.FromPort == port || ed.ToPort == port);
            if (port.Side == PortSide.Input) node.Inputs.Remove(port);
            else node.Outputs.Remove(port);

            AutoSizeForPorts(node, allowShrink: true); // shrink if lots of space
            EnsureProxiesMatch(node);
            Invalidate();
        }


        private static string UniquePortName(IEnumerable<string> existing, string baseName) {
            var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            if (!set.Contains(baseName)) return baseName;
            int i = 1;
            while (set.Contains($"{baseName}{i}")) i++;
            return $"{baseName}{i}";
        }

        private void EnsureProxiesMatch(Node owner) {
            if (owner.Inner == null) return;
            var g = owner.Inner;

            var inlets = g.Nodes.Where(n => n.IsProxy && n.ProxyIsInlet).OrderBy(n => n.ProxyIndex).ToList();
            var outlets = g.Nodes.Where(n => n.IsProxy && !n.ProxyIsInlet).OrderBy(n => n.ProxyIndex).ToList();

            // Inlets sync
            for (int i = inlets.Count; i < owner.Inputs.Count; i++) {
                var parentPort = owner.Inputs[i];
                var pn = new Node(parentPort.Name, PointF.Empty, createDefaultPorts: false) { IsProxy = true, ProxyIsInlet = true, ProxyIndex = i, Id = "inlet_" + i };
                pn.Outputs.Add(new Port(pn, PortSide.Output, parentPort.Name, parentPort.TypeName));
                g.Nodes.Add(pn);
            }
            for (int i = owner.Inputs.Count; i < inlets.Count; i++) {
                var pn = inlets[i];
                g.Edges.RemoveAll(ed => ed.FromNode == pn || ed.ToNode == pn);
                g.Nodes.Remove(pn);
            }
            for (int i = 0; i < owner.Inputs.Count; i++) {
                var parentPort = owner.Inputs[i];
                var pn = g.Nodes.First(n => n.IsProxy && n.ProxyIsInlet && n.ProxyIndex == i);
                pn.Title = parentPort.Name;
                pn.Outputs[0].Name = parentPort.Name;
                pn.Outputs[0].TypeName = parentPort.TypeName;
            }

            // Outlets sync
            for (int i = outlets.Count; i < owner.Outputs.Count; i++) {
                var parentPort = owner.Outputs[i];
                var pn = new Node(parentPort.Name, PointF.Empty, createDefaultPorts: false) { IsProxy = true, ProxyIsInlet = false, ProxyIndex = i, Id = "outlet_" + i };
                pn.Inputs.Add(new Port(pn, PortSide.Input, parentPort.Name, parentPort.TypeName));
                g.Nodes.Add(pn);
            }
            for (int i = owner.Outputs.Count; i < outlets.Count; i++) {
                var pn = outlets[i];
                g.Edges.RemoveAll(ed => ed.FromNode == pn || ed.ToNode == pn);
                g.Nodes.Remove(pn);
            }
            for (int i = 0; i < owner.Outputs.Count; i++) {
                var parentPort = owner.Outputs[i];
                var pn = g.Nodes.First(n => n.IsProxy && !n.ProxyIsInlet && n.ProxyIndex == i);
                pn.Title = parentPort.Name;
                pn.Inputs[0].Name = parentPort.Name;
                pn.Inputs[0].TypeName = parentPort.TypeName;
            }
        }

        private void AutoSizeForPorts(Node n, bool allowShrink) {
            // Use current dynamic title height (already updated during paint/layout cycle)
            float titleH = n.TitleH <= 0 ? 28f : n.TitleH;
            float topPad = 10f, botPad = 10f, rowH = 30f;

            int rows = Math.Max(n.Inputs.Count, n.Outputs.Count);
            float needed = titleH + topPad + rows * rowH + botPad;

            float newH = allowShrink ? needed : Math.Max(n.Size.Height, needed);
            n.Size = new SizeF(Math.Max(n.Size.Width, 140f), Math.Max(newH, 80f));
            n.LayoutPorts();
        }

        private RectangleF InnerWorkingRect() {
            const float headerH = 28f, topPad = 60f, botPad = 40f, sidePad = 160f;
            float x = -current.ViewOffset.X + sidePad;
            float y = -current.ViewOffset.Y + headerH + topPad;
            float w = Math.Max(200f, ClientSize.Width - sidePad * 2f);
            float h = Math.Max(160f, ClientSize.Height - (headerH + topPad + botPad));
            return new RectangleF(x, y, w, h);
        }
        private static RectangleF BoundsOf(IEnumerable<Node> nodes) {
            bool any = false;
            float minx = float.MaxValue, miny = float.MaxValue, maxx = float.MinValue, maxy = float.MinValue;
            foreach (var n in nodes) {
                any = true;
                minx = Math.Min(minx, n.Position.X);
                miny = Math.Min(miny, n.Position.Y);
                maxx = Math.Max(maxx, n.Position.X + n.Size.Width);
                maxy = Math.Max(maxy, n.Position.Y + n.Size.Height);
            }
            return any ? RectangleF.FromLTRB(minx, miny, maxx, maxy) : RectangleF.Empty;
        }
        private static void TranslateNodes(IEnumerable<Node> nodes, float dx, float dy) {
            foreach (var n in nodes) {
                n.Position = new PointF(n.Position.X + dx, n.Position.Y + dy);
                n.LayoutPorts();
            }
        }
        private void PlaceNodesIntoInnerArea(List<Node> nodes) {
            if (nodes.Count == 0) return;

            var area = InnerWorkingRect();
            var bb = BoundsOf(nodes);
            if (bb.Width <= 0 || bb.Height <= 0) return;

            var bbCenter = new PointF(bb.Left + bb.Width / 2f, bb.Top + bb.Height / 2f);
            var areaCenter = new PointF(area.Left + area.Width / 2f, area.Top + area.Height / 2f);
            TranslateNodes(nodes, areaCenter.X - bbCenter.X, areaCenter.Y - bbCenter.Y);

            bb = BoundsOf(nodes);
            float pad = 8f, dx = 0f, dy = 0f;
            if (bb.Left < area.Left + pad) dx += (area.Left + pad) - bb.Left;
            if (bb.Right > area.Right - pad) dx += (area.Right - pad) - bb.Right;
            if (bb.Top < area.Top + pad) dy += (area.Top + pad) - bb.Top;
            if (bb.Bottom > area.Bottom - pad) dy += (area.Bottom - pad) - bb.Bottom;
            if (dx != 0 || dy != 0) TranslateNodes(nodes, dx, dy);
        }

        private Dictionary<ResizeHandle, RectangleF> GetHandleRects(Node n) {
            var r = n.Bounds;
            const float s = 8f;
            float half = s / 2f;

            var centers = new Dictionary<ResizeHandle, PointF>
    {
        { ResizeHandle.NW, new PointF(r.Left,  r.Top) },
        { ResizeHandle.N,  new PointF(r.Left + r.Width/2f, r.Top) },
        { ResizeHandle.NE, new PointF(r.Right, r.Top) },
        { ResizeHandle.W,  new PointF(r.Left,  r.Top + r.Height/2f) },
        { ResizeHandle.E,  new PointF(r.Right, r.Top + r.Height/2f) },
        { ResizeHandle.SW, new PointF(r.Left,  r.Bottom) },
        { ResizeHandle.S,  new PointF(r.Left + r.Width/2f, r.Bottom) },
        { ResizeHandle.SE, new PointF(r.Right, r.Bottom) },
    };

            var rects = new Dictionary<ResizeHandle, RectangleF>();
            foreach (var kv in centers)
                rects[kv.Key] = new RectangleF(kv.Value.X - half, kv.Value.Y - half, s, s);

            return rects;
        }

        private ResizeHandle GetResizeHandle(Node n, PointF world) {
            if (n.IsProxy) return ResizeHandle.None;
            foreach (var kv in GetHandleRects(n))
                if (kv.Value.Contains(world)) return kv.Key;
            return ResizeHandle.None;
        }

        private static Cursor CursorForHandle(ResizeHandle h) => h switch {
            ResizeHandle.N or ResizeHandle.S => Cursors.SizeNS,
            ResizeHandle.E or ResizeHandle.W => Cursors.SizeWE,
            ResizeHandle.NE or ResizeHandle.SW => Cursors.SizeNESW,
            ResizeHandle.NW or ResizeHandle.SE => Cursors.SizeNWSE,
            _ => Cursors.Default
        };

        private void ApplyResize(Node n, ResizeHandle h, float dx, float dy) {
            var pos = resizeStartPos;
            var size = resizeStartSize;

            // Horizontal
            switch (h) {
                case ResizeHandle.E:
                case ResizeHandle.NE:
                case ResizeHandle.SE:
                    size.Width = Math.Max(MinNodeW, resizeStartSize.Width + dx);
                    break;
                case ResizeHandle.W:
                case ResizeHandle.NW:
                case ResizeHandle.SW:
                    size.Width = Math.Max(MinNodeW, resizeStartSize.Width - dx);
                    pos.X = resizeStartPos.X + (resizeStartSize.Width - size.Width);
                    break;
            }

            // Vertical
            switch (h) {
                case ResizeHandle.S:
                case ResizeHandle.SE:
                case ResizeHandle.SW:
                    size.Height = Math.Max(MinNodeH, resizeStartSize.Height + dy);
                    break;
                case ResizeHandle.N:
                case ResizeHandle.NE:
                case ResizeHandle.NW:
                    size.Height = Math.Max(MinNodeH, resizeStartSize.Height - dy);
                    pos.Y = resizeStartPos.Y + (resizeStartSize.Height - size.Height);
                    break;
            }

            // Ensure enough room for title + padding + at least 1 port row
            int rows = Math.Max(n.Inputs.Count, n.Outputs.Count);
            float topPad = 10f, botPad = 10f, rowH = 30f;
            float dynMinH = Math.Max(MinNodeH, n.TitleH + topPad + Math.Max(1, rows) * rowH + botPad);
            size.Height = Math.Max(dynMinH, size.Height);

            n.Position = pos;
            n.Size = size;
            n.LayoutPorts();
        }

        // Deep-clone an entire graph (nodes, ports, edges, and nested inners).
        private Graph CloneGraphRecursive(Graph src, Graph? parent, Node? owner) {
            var g = new Graph { Parent = parent, Owner = owner, ViewOffset = src.ViewOffset };
            var map = new Dictionary<Node, Node>();

            // 1) Clone nodes (shallow: geometry/ports/proxy flags)
            foreach (var n in src.Nodes) {
                var cn = new Node(n.Title, n.Position, createDefaultPorts: false) {
                    Size = n.Size,
                    IsProxy = n.IsProxy,
                    ProxyIsInlet = n.ProxyIsInlet,
                    ProxyIndex = n.ProxyIndex
                };
                foreach (var p in n.Inputs) cn.Inputs.Add(new Port(cn, PortSide.Input, p.Name, p.TypeName));
                foreach (var p in n.Outputs) cn.Outputs.Add(new Port(cn, PortSide.Output, p.Name, p.TypeName));
                cn.LayoutPorts();

                g.Nodes.Add(cn);
                map[n] = cn;
            }

            // 2) Clone edges (by matching port names)
            foreach (var e in src.Edges) {
                var fn = map[e.FromNode];
                var tn = map[e.ToNode];
                var fp = fn.Outputs.FirstOrDefault(p => p.Name == e.FromPort.Name);
                var tp = tn.Inputs.FirstOrDefault(p => p.Name == e.ToPort.Name);
                if (fp != null && tp != null) g.Edges.Add(new Edge(fp, tp));
            }

            // 3) Recurse for each node's inner graph
            foreach (var n in src.Nodes) {
                if (n.Inner != null) {
                    var cn = map[n];
                    cn.Inner = CloneGraphRecursive(n.Inner, g, cn);
                }
            }

            return g;
        }

        // Duplicate one node in the current graph; places copy offset and selects it.
        private void DuplicateNode(Node src) {
            if (src.IsProxy) { System.Media.SystemSounds.Beep.Play(); return; }

            var dup = new Node(src.Title + " Copy", new PointF(src.Position.X + 30, src.Position.Y + 30), createDefaultPorts: false) {
                Size = src.Size
            };
            foreach (var p in src.Inputs) dup.Inputs.Add(new Port(dup, PortSide.Input, p.Name, p.TypeName));
            foreach (var p in src.Outputs) dup.Outputs.Add(new Port(dup, PortSide.Output, p.Name, p.TypeName));
            dup.LayoutPorts();

            if (src.Inner != null) {
                dup.Inner = CloneGraphRecursive(src.Inner, current, dup);
                EnsureProxiesMatch(dup); // keep proxies in sync with duplicated ports
            }

            current.Nodes.Add(dup);
            selection.Clear();
            selection.Add(dup);
            Invalidate();
        }
    }
}