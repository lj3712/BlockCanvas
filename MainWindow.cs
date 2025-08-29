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
    public sealed class MainWindow : Form {
        private readonly Canvas canvas = new();
        private string? currentPath;

        public MainWindow() {
            Text = "Block Canvas � pan/scroll � grouping � dynamic ports � typed � hierarchical";
            ClientSize = new Size(1100, 740);
            StartPosition = FormStartPosition.CenterScreen;

            // Menu
            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("&File");
            var miNew = new ToolStripMenuItem("&New", null, (_, __) => NewLayout()) { ShortcutKeys = Keys.Control | Keys.N };
            var miOpen = new ToolStripMenuItem("&Open�", null, (_, __) => OpenLayout()) { ShortcutKeys = Keys.Control | Keys.O };
            var miSaveA = new ToolStripMenuItem("Save &As�", null, (_, __) => SaveAsLayout()) { ShortcutKeys = Keys.Control | Keys.S };
            var miExit = new ToolStripMenuItem("E&xit", null, (_, __) => Close());
            file.DropDownItems.AddRange(new ToolStripItem[] { miNew, miOpen, new ToolStripSeparator(), miSaveA, new ToolStripSeparator(), miExit });
            menu.Items.Add(file);
            MainMenuStrip = menu;

            // Status
            var status = new StatusStrip();
            var hint = new ToolStripStatusLabel("Middle-drag or Space+Left: pan � Wheel: scroll (Shift=horizontal) � Ctrl+G: Group");
            status.Items.Add(hint);

            // Canvas
            canvas.Dock = DockStyle.Fill;

            Controls.Add(canvas);
            Controls.Add(status);
            Controls.Add(menu);

        }

        private void NewLayout() {
            if (!ConfirmLoseChanges()) return;
            canvas.NewGraph();
            currentPath = null;
            Text = "Block Canvas � (untitled)";
        }

        private void OpenLayout() {
            if (!ConfirmLoseChanges()) return;
            using var ofd = new OpenFileDialog { Filter = "BlockCanvas S-expression (*.bcanvas)|*.bcanvas|BlockCanvas JSON (*.bcanvas.json)|*.bcanvas.json|JSON (*.json)|*.json|All files (*.*)|*.*" };
            if (ofd.ShowDialog(this) == DialogResult.OK) {
                try {
                    canvas.LoadFrom(ofd.FileName);
                    currentPath = ofd.FileName;
                    Text = $"Block Canvas � {Path.GetFileName(currentPath)}";
                }
                catch (Exception ex) {
                    MessageBox.Show(this, "Failed to load:\n\n" + ex, "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SaveAsLayout() {
            using var sfd = new SaveFileDialog { Filter = "BlockCanvas S-expression (*.bcanvas)|*.bcanvas|BlockCanvas JSON (*.bcanvas.json)|*.bcanvas.json|JSON (*.json)|*.json|All files (*.*)|*.*", FileName = Path.GetFileName(currentPath ?? "layout.bcanvas") };
            if (sfd.ShowDialog(this) == DialogResult.OK) {
                try {
                    canvas.SaveTo(sfd.FileName);
                    currentPath = sfd.FileName;
                    Text = $"Block Canvas � {Path.GetFileName(currentPath)}";
                }
                catch (Exception ex) {
                    MessageBox.Show(this, "Failed to save:\n\n" + ex, "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private bool ConfirmLoseChanges() {
            var r = MessageBox.Show(this, "Discard current layout?", "Confirm", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            return r == DialogResult.OK;
        }
    }
}