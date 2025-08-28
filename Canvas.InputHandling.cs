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
                        if (hitNode != null) {
                            current.Edges.RemoveAll(ed => ed.FromNode == hitNode || ed.ToNode == hitNode);
                            current.Nodes.Remove(hitNode);
                            selection.Remove(hitNode);
                            Invalidate();
                        }
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
    }
}