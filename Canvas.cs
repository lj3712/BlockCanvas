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
    public sealed class Canvas : Panel {
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


        // ===== UI events =====

        private void OnMouseDoubleClick(object? sender, MouseEventArgs e) {
            if (e.Button != MouseButtons.Left) return;

            // Double-click breadcrumb bar => up
            if (headerRect.Contains(e.Location) && CurrentLevel() > 0) { ZoomOut(); return; }

            // Double-click a node => dive in (world coords)
            var world = ScreenToWorld(e.Location);
            Node? hitNode = current.Nodes.LastOrDefault(n => !n.IsProxy && n.HitBody(world));
            if (hitNode != null) ZoomInto(hitNode);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e) {
            if (e.Control && e.KeyCode == Keys.G) {
                GroupSelectionIntoBlock();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Delete && selection.Count > 0) {
                foreach (var node in selection.ToList()) {
                    if (node.IsProxy) continue;
                    current.Edges.RemoveAll(ed => ed.FromNode == node || ed.ToNode == node);
                    current.Nodes.Remove(node);
                }
                selection.Clear();
                Invalidate();
            }
            if (e.KeyCode == Keys.Escape) {
                if (panning) { panning = false; Invalidate(); return; }
                if (current.Owner != null) ZoomOut();
            }
        }

        private void OnMouseDown(object? sender, MouseEventArgs e) {
            Focus();
            lastMouseWorld = ScreenToWorld(e.Location);

            // Start panning with middle button or Space+Left
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Space) == Keys.Space)) {
                panning = true;
                panMouseStart = e.Location;
                viewStart = current.ViewOffset;
                return;
            }

            Port? hitPort = FindPortAt(lastMouseWorld);
            Node? hitNode = current.Nodes.LastOrDefault(n => !n.IsProxy && n.HitBody(lastMouseWorld));

            if (e.Button == MouseButtons.Left) {
                // world coords already in lastMouseWorld
                Node? hitNodeForResize = current.Nodes.LastOrDefault(n => !n.IsProxy && n.HitBody(lastMouseWorld));
                if (hitNodeForResize != null) {
                    var h = GetResizeHandle(hitNodeForResize, lastMouseWorld);
                    if (h != ResizeHandle.None) {
                        resizingNode = hitNodeForResize;
                        activeHandle = h;
                        resizeStartMouseWorld = lastMouseWorld;
                        resizeStartPos = hitNodeForResize.Position;
                        resizeStartSize = hitNodeForResize.Size;
                        Invalidate();
                        return;
                    }
                }
                if (hitPort != null) {
                    connectStartPort = hitPort;
                    Invalidate();
                    return;
                }

                if (hitNode != null && hitNode.HitTitleBar(lastMouseWorld)) {
                    // selection handling
                    if ((ModifierKeys & Keys.Control) == Keys.Control) {
                        if (!selection.Add(hitNode)) selection.Remove(hitNode);
                    }
                    else {
                        if (!selection.Contains(hitNode)) { selection.Clear(); selection.Add(hitNode); }
                    }

                    // start drag for the whole selection (world coords)
                    draggingSelection = true;
                    dragStartMouseWorld = lastMouseWorld;
                    dragStartPos.Clear();
                    foreach (var n in selection) dragStartPos[n] = n.Position;

                    Invalidate();
                }
                else {
                    // Clicking blank or node body: select single (unless Ctrl toggling)
                    if ((ModifierKeys & Keys.Control) == Keys.Control) {
                        if (hitNode != null) {
                            if (!selection.Add(hitNode)) selection.Remove(hitNode);
                        }
                    }
                    else {
                        selection.Clear();
                        if (hitNode != null) selection.Add(hitNode);
                    }
                    Invalidate();
                }
            }

            if (e.Button == MouseButtons.Right) {
                // Context menus
                if (hitPort != null) {
                    var menu = new ContextMenuStrip();

                    var miRename = new ToolStripMenuItem("Rename…", null, (_, __) => {
                        string? s = Microsoft.VisualBasic.Interaction.InputBox("Port name:", "Rename Port", hitPort.Name);
                        if (!string.IsNullOrWhiteSpace(s)) {
                            hitPort.Name = s.Trim();
                            Invalidate();
                        }
                    });
                    menu.Items.Add(miRename);

                    var miDelete = new ToolStripMenuItem("Delete Port", null, (_, __) => DeletePort(hitPort)) { Enabled = !hitPort.Owner.IsProxy };
                    menu.Items.Add(miDelete);

                    menu.Items.Add("Delete", null, (_, __) => {
                        current.Edges.RemoveAll(ed => ed.FromNode == hitNode || ed.ToNode == hitNode);
                        current.Nodes.Remove(hitNode);
                        selection.Remove(hitNode);
                        Invalidate();
                    });



                    menu.Items.Add(new ToolStripSeparator());
                    var typeMenu = new ToolStripMenuItem("Type");

                    // Fundamentals
                    foreach (var t in TypeUtil.Fundamentals) {
                        var item = new ToolStripMenuItem(t) { Checked = string.Equals(hitPort.TypeName, t, StringComparison.Ordinal) };
                        item.Click += (_, __) => { hitPort.TypeName = t; Invalidate(); };
                        typeMenu.DropDownItems.Add(item);
                    }

                    // Composite…
                    typeMenu.DropDownItems.Add(new ToolStripSeparator());
                    typeMenu.DropDownItems.Add(new ToolStripMenuItem("Set Composite Type…", null, (_, __) => {
                        string? s = Microsoft.VisualBasic.Interaction.InputBox("Composite type name:", "Set Port Type", hitPort.TypeName);
                        if (!string.IsNullOrWhiteSpace(s)) {
                            hitPort.TypeName = TypeUtil.Normalize(s);
                            Invalidate();
                        }
                    }));

                    menu.Items.Add(typeMenu);

                    menu.Show(this, e.Location);
                    return;
                }

                if (hitNode != null) {
                    if (!selection.Contains(hitNode) && (ModifierKeys & Keys.Control) != Keys.Control) {
                        selection.Clear();
                        selection.Add(hitNode);
                    }

                    var menu = new ContextMenuStrip();
                    menu.Items.Add("Rename…", null, (_, __) => {
                        string? s = Microsoft.VisualBasic.Interaction.InputBox("Node title:", "Rename Node", hitNode.Title);
                        if (!string.IsNullOrWhiteSpace(s)) { hitNode.Title = s.Trim(); Invalidate(); }
                    });
                    menu.Items.Add("Delete", null, (_, __) => {
                        current.Edges.RemoveAll(ed => ed.FromNode == hitNode || ed.ToNode == hitNode);
                        current.Nodes.Remove(hitNode);
                        selection.Remove(hitNode);
                        Invalidate();
                    });

                    // NEW: Duplicate
                    var miDup = new ToolStripMenuItem("Duplicate", null, (_, __) => DuplicateNode(hitNode)) {
                        Enabled = !hitNode.IsProxy
                    };
                    menu.Items.Add(miDup);


                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Add Input Port", null, (_, __) => AddInputPort(hitNode));
                    menu.Items.Add("Add Output Port", null, (_, __) => AddOutputPort(hitNode));

                    var rmIn = new ToolStripMenuItem("Remove Input Port");
                    for (int i = 0; i < hitNode.Inputs.Count; i++) {
                        var pRef = hitNode.Inputs[i];
                        int idx = i;
                        var item = new ToolStripMenuItem($"{idx}: {pRef.Name}", null, (_, __) => DeletePort(pRef));
                        rmIn.DropDownItems.Add(item);
                    }
                    rmIn.Enabled = hitNode.Inputs.Count > 0;
                    menu.Items.Add(rmIn);

                    var rmOut = new ToolStripMenuItem("Remove Output Port");
                    for (int i = 0; i < hitNode.Outputs.Count; i++) {
                        var pRef = hitNode.Outputs[i];
                        int idx = i;
                        var item = new ToolStripMenuItem($"{idx}: {pRef.Name}", null, (_, __) => DeletePort(pRef));
                        rmOut.DropDownItems.Add(item);
                    }
                    rmOut.Enabled = hitNode.Outputs.Count > 0;
                    menu.Items.Add(rmOut);

                    if (selection.Count > 0 && selection.Any(n => !n.IsProxy)) {
                        menu.Items.Add(new ToolStripSeparator());
                        menu.Items.Add("Group Selected into Block…", null, (_, __) => GroupSelectionIntoBlock());
                    }

                    // View helpers
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Reset View (this level)", null, (_, __) => { current.ViewOffset = PointF.Empty; Invalidate(); });

                    menu.Show(this, e.Location);
                }
                else {
                    // Blank-canvas context menu (no demo entries)
                    var menu = new ContextMenuStrip();

                    void AddAt(string title) {
                        var world = ScreenToWorld(e.Location);
                        var pos = new PointF(world.X - 30, world.Y - 16);
                        var n = new Node(title, pos, createDefaultPorts: false);
                        AddNode(n);
                    }

                    menu.Items.Add("Add Block Here", null, (_, __) => AddAt("Block"));

                    if (selection.Count > 0 && selection.Any(n => !n.IsProxy)) {
                        menu.Items.Add(new ToolStripSeparator());
                        menu.Items.Add("Group Selected into Block…", null, (_, __) => GroupSelectionIntoBlock());
                    }

                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Reset View (this level)", null, (_, __) => { current.ViewOffset = PointF.Empty; Invalidate(); });

                    menu.Show(this, e.Location);
                }
            }
        }

        private void OnMouseMove(object? sender, MouseEventArgs e) {
            lastMouseWorld = ScreenToWorld(e.Location);
            // Resizing?
            if (resizingNode != null && activeHandle != ResizeHandle.None) {
                var dx = lastMouseWorld.X - resizeStartMouseWorld.X;
                var dy = lastMouseWorld.Y - resizeStartMouseWorld.Y;
                ApplyResize(resizingNode, activeHandle, dx, dy);
                Invalidate();
                return;
            }

            // Not resizing: update cursor if hovering a handle
            Node? hoverNode = current.Nodes.LastOrDefault(n => !n.IsProxy && n.HitBody(lastMouseWorld));
            if (hoverNode != null) {
                var h = GetResizeHandle(hoverNode, lastMouseWorld);
                Cursor = CursorForHandle(h);
            }
            else if (!panning) {
                Cursor = Cursors.Default;
            }

            if (panning) {
                var dx = e.Location.X - panMouseStart.X;
                var dy = e.Location.Y - panMouseStart.Y;
                current.ViewOffset = new PointF(viewStart.X + dx, viewStart.Y + dy);
                Invalidate();
                return;
            }

            if (draggingSelection && selection.Count > 0) {
                var delta = new PointF(lastMouseWorld.X - dragStartMouseWorld.X, lastMouseWorld.Y - dragStartMouseWorld.Y);
                foreach (var kv in dragStartPos) {
                    kv.Key.Position = new PointF(kv.Value.X + delta.X, kv.Value.Y + delta.Y);
                    kv.Key.LayoutPorts();
                }
                Invalidate();
            }
            else {
                Invalidate();
            }
        }

        private void OnMouseUp(object? sender, MouseEventArgs e) {
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Space) == Keys.Space)) {
                panning = false;
                return;
            }

            if (e.Button == MouseButtons.Left) {
                draggingSelection = false;
                if (resizingNode != null) {
                    resizingNode = null;
                    activeHandle = ResizeHandle.None;
                    Invalidate();
                    return;
                }
            }

            if (e.Button != MouseButtons.Left) return;

            if (connectStartPort != null) {
                Port? endPort = FindPortAt(lastMouseWorld);
                if (endPort != null && endPort != connectStartPort) {
                    Port? outPort = null, inPort = null;
                    if (connectStartPort.Side == PortSide.Output && endPort.Side == PortSide.Input) { outPort = connectStartPort; inPort = endPort; }
                    else if (connectStartPort.Side == PortSide.Input && endPort.Side == PortSide.Output) { outPort = endPort; inPort = connectStartPort; }

                    if (outPort != null && inPort != null) {
                        if (!TypeUtil.Compatible(outPort.TypeName, inPort.TypeName)) {
                            System.Media.SystemSounds.Beep.Play();
                        }
                        else {
                            var existing = FindEdge(outPort, inPort);
                            if (existing != null) current.Edges.Remove(existing);
                            else {
                                current.Edges.RemoveAll(ed => ed.ToPort == inPort); // one inbound per input
                                current.Edges.Add(new Edge(outPort, inPort));
                            }
                        }
                    }
                }

                connectStartPort = null;
                Invalidate();
            }
        }

        private void OnMouseWheel(object? sender, MouseEventArgs e) {
            // Scroll (Shift = horizontal)
            float step = 60f * Math.Max(1, Math.Min(3, Math.Abs(e.Delta) / 120)); // 60, 120, 180 px
            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                current.ViewOffset = new PointF(current.ViewOffset.X + (e.Delta > 0 ? step : -step), current.ViewOffset.Y);
            else
                current.ViewOffset = new PointF(current.ViewOffset.X, current.ViewOffset.Y + (e.Delta > 0 ? step : -step));
            Invalidate();
        }

        // ===== Grouping (unchanged logic except it uses world coords everywhere) =====

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

        // ===== Zooming / hierarchy =====

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

        // ===== Port/proxy helpers =====

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

        // ===== Drawing =====

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

            // Place proxies in world coords near nominal “viewport” edges (using current.ViewOffset)
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
            float radius = 10f;

            using var path = RoundedRect(rect, radius);
            var selected = selection.Contains(n);
            using var fill = new LinearGradientBrush(rect, Color.FromArgb(58, 62, 70), Color.FromArgb(44, 47, 53), LinearGradientMode.Vertical);
            using var border = new Pen(selected ? Color.FromArgb(120, 190, 255) : Color.FromArgb(85, 90, 100), selected ? 2.5f : 1.5f);

            g.FillPath(fill, path);
            g.DrawPath(border, path);

            // Title bar
            var titleRect = new RectangleF(rect.X, rect.Y, rect.Width, n.TitleH);
            using var titleBrush = new SolidBrush(Color.FromArgb(70, 74, 82));
            using var titleBorder = new Pen(Color.FromArgb(90, 95, 105), 1);
            g.FillRectangle(titleBrush, titleRect);
            g.DrawLine(titleBorder, titleRect.Left, titleRect.Bottom, titleRect.Right, titleRect.Bottom);

            using var textBrush = new SolidBrush(Color.White);
            var sfTitle = new StringFormat {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.LineLimit
            };
            g.DrawString(n.Title, titleFont, textBrush,
                         new RectangleF(titleRect.X + 8, titleRect.Y + 6, titleRect.Width - 16, titleRect.Height - 8),
                         sfTitle);
            foreach (var p in n.Inputs) DrawPort(g, p, isInput: true);
            foreach (var p in n.Outputs) DrawPort(g, p, isInput: false);

            if (selection.Contains(n))
                DrawResizeHandles(g, n);
        }

        private void DrawPort(Graphics g, Port port, bool isInput) {
            float r = port.CornerRadius;
            var rect = port.VisualRect;

            var baseC = TypeUtil.BaseColor(port.TypeName);
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
            g.DrawString($"{port.Name} : {TypeUtil.Short(port.TypeName)}", font, tb, rect, sf);
        }

        private void DrawEdge(Graphics g, Edge e) {
            using var pen = new Pen(Color.FromArgb(170, 170, 190), 2);
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

        // ===== Layout helpers (inner placement & size) =====

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

        private void DrawResizeHandles(Graphics g, Node n) {
            if (n.IsProxy) return;
            using var br = new SolidBrush(Color.White);
            using var pen = new Pen(Color.Black, 1f);
            foreach (var rect in GetHandleRects(n).Values) {
                g.FillRectangle(br, rect);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }

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


        // ===== Serialization (JSON DTO mapping) =====
        private static LayoutGraphDto ToDto(Graph g) {
            var dto = new LayoutGraphDto {
                VX = g.ViewOffset.X,   // NEW
                VY = g.ViewOffset.Y    // NEW
            };

            foreach (var n in g.Nodes) {
                var nDto = new NodeDto {
                    Id = n.Id,
                    Title = n.Title,
                    X = n.Position.X,
                    Y = n.Position.Y,
                    W = n.Size.Width,
                    H = n.Size.Height,
                    IsProxy = n.IsProxy,
                    ProxyIsInlet = n.ProxyIsInlet,
                    ProxyIndex = n.ProxyIndex,
                    Inputs = n.Inputs.Select(p => new PortDef { Name = p.Name, Type = p.TypeName }).ToList(),
                    Outputs = n.Outputs.Select(p => new PortDef { Name = p.Name, Type = p.TypeName }).ToList(),
                    Inner = n.Inner != null ? ToDto(n.Inner) : null
                };
                dto.Nodes.Add(nDto);
            }
            foreach (var e in g.Edges) {
                dto.Edges.Add(new EdgeDto {
                    From = new PortRefDto { NodeId = e.FromNode.Id, Side = "Output", Port = e.FromPort.Name },
                    To = new PortRefDto { NodeId = e.ToNode.Id, Side = "Input", Port = e.ToPort.Name }
                });
            }
            return dto;
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

            // 3) Recurse for each node’s inner graph
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


        private static Graph FromDto(LayoutGraphDto dto, Graph? parent, Node? owner) {
            var g = new Graph { Parent = parent, Owner = owner };
            g.ViewOffset = new PointF(dto.VX, dto.VY);
            var idMap = new Dictionary<string, Node>();

            foreach (var nDto in dto.Nodes) {
                var n = new Node(nDto.Title, new PointF(nDto.X, nDto.Y), createDefaultPorts: false) {
                    Id = string.IsNullOrWhiteSpace(nDto.Id) ? Guid.NewGuid().ToString("N") : nDto.Id,
                    IsProxy = nDto.IsProxy,
                    ProxyIsInlet = nDto.ProxyIsInlet,
                    ProxyIndex = nDto.ProxyIndex,
                    Size = new SizeF(nDto.W <= 0 ? 190 : nDto.W, nDto.H <= 0 ? 98 : nDto.H)
                };

                foreach (var def in nDto.Inputs)
                    n.Inputs.Add(new Port(n, PortSide.Input, def.Name, def.Type));
                foreach (var def in nDto.Outputs)
                    n.Outputs.Add(new Port(n, PortSide.Output, def.Name, def.Type));

                n.LayoutPorts();

                g.Nodes.Add(n);
                idMap[n.Id] = n;

                if (nDto.Inner != null)
                    n.Inner = FromDto(nDto.Inner, g, n);
            }

            foreach (var eDto in dto.Edges) {
                if (!idMap.TryGetValue(eDto.From.NodeId, out var fromNode)) continue;
                if (!idMap.TryGetValue(eDto.To.NodeId, out var toNode)) continue;

                var fromPort = fromNode.Outputs.FirstOrDefault(p => p.Name == eDto.From.Port);
                var toPort = toNode.Inputs.FirstOrDefault(p => p.Name == eDto.To.Port);
                if (fromPort == null || toPort == null) continue;

                g.Edges.Add(new Edge(fromPort, toPort));
            }

            return g;
        }
    }
}
