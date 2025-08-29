using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace BlockCanvas {
    public static class SExpressionSerializer {
        public static void SaveLayout(LayoutDto layout, string path) {
            var sexpr = SerializeLayout(layout);
            File.WriteAllText(path, sexpr);
        }

        public static LayoutDto LoadLayout(string path) {
            var content = File.ReadAllText(path);
            return DeserializeLayout(content);
        }

        private static string SerializeLayout(LayoutDto layout) {
            var sb = new StringBuilder();
            sb.AppendLine("(schema ImpetusProject");
            sb.AppendLine($"  (version \"{layout.Version}\")");
            
            if (layout.Graph.Nodes.Count > 0 || layout.Graph.Edges.Count > 0) {
                sb.AppendLine("  (blocks");
                SerializeGraph(sb, layout.Graph, "    ");
                sb.AppendLine("  )");
            }
            
            sb.Append(")");
            return sb.ToString();
        }

        private static void SerializeGraph(StringBuilder sb, LayoutGraphDto graph, string indent) {
            // Add view offset if not default
            if (graph.VX != 0f || graph.VY != 0f) {
                sb.AppendLine($"{indent}(view-offset {FormatFloat(graph.VX)} {FormatFloat(graph.VY)})");
            }

            foreach (var node in graph.Nodes) {
                SerializeNode(sb, node, indent);
            }

            if (graph.Edges.Count > 0) {
                sb.AppendLine($"{indent}(wires");
                foreach (var edge in graph.Edges) {
                    sb.AppendLine($"{indent}  ({edge.From.NodeId}.{edge.From.Port} -> {edge.To.NodeId}.{edge.To.Port})");
                }
                sb.AppendLine($"{indent})");
            }
        }

        private static void SerializeNode(StringBuilder sb, NodeDto node, string indent) {
            sb.AppendLine($"{indent}(block \"{node.Id}\"");
            sb.AppendLine($"{indent}  (title \"{node.Title}\")");
            sb.AppendLine($"{indent}  (position {FormatFloat(node.X)} {FormatFloat(node.Y)})");
            sb.AppendLine($"{indent}  (size {FormatFloat(node.W)} {FormatFloat(node.H)})");
            
            if (node.Type != "Regular") {
                sb.AppendLine($"{indent}  (type \"{node.Type}\")");
            }
            
            if (node.IsPermanent) {
                sb.AppendLine($"{indent}  (is-permanent true)");
            }
            
            if (node.ConstValue != "0") {
                sb.AppendLine($"{indent}  (const-value \"{node.ConstValue}\")");
            }
            
            if (!string.IsNullOrEmpty(node.MarshallerOutputType)) {
                sb.AppendLine($"{indent}  (marshaller-output-type \"{node.MarshallerOutputType}\")");
            }
            
            if (node.IsProxy) {
                sb.AppendLine($"{indent}  (is-proxy true)");
                sb.AppendLine($"{indent}  (proxy-is-inlet {(node.ProxyIsInlet ? "true" : "false")})");
                sb.AppendLine($"{indent}  (proxy-index {node.ProxyIndex})");
            }

            if (node.Inputs.Count > 0) {
                sb.AppendLine($"{indent}  (inputs");
                foreach (var port in node.Inputs) {
                    SerializePort(sb, port, indent + "    ");
                }
                sb.AppendLine($"{indent}  )");
            }

            if (node.Outputs.Count > 0) {
                sb.AppendLine($"{indent}  (outputs");
                foreach (var port in node.Outputs) {
                    SerializePort(sb, port, indent + "    ");
                }
                sb.AppendLine($"{indent}  )");
            }

            if (node.Inner != null) {
                sb.AppendLine($"{indent}  (inner");
                SerializeGraph(sb, node.Inner, indent + "    ");
                sb.AppendLine($"{indent}  )");
            }

            sb.AppendLine($"{indent})");
        }

        private static void SerializePort(StringBuilder sb, PortDef port, string indent) {
            var parts = new List<string> { $"\"{port.Name}\"" };
            
            if (port.BitLength != 1) {
                parts.Add($"[{port.BitLength}]");
            }
            
            if (!string.IsNullOrEmpty(port.UserTypeName)) {
                parts.Add($"(user-type \"{port.UserTypeName}\")");
            }
            
            if (port.Width != 108f) {
                parts.Add($"(width {FormatFloat(port.Width)})");
            }

            sb.AppendLine($"{indent}({string.Join(" ", parts)})");
        }

        private static string FormatFloat(float value) {
            return value.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static LayoutDto DeserializeLayout(string content) {
            var tokens = Tokenize(content);
            var expr = ParseSExpression(tokens);
            return ParseLayout(expr);
        }

        private static List<string> Tokenize(string content) {
            var tokens = new List<string>();
            var current = new StringBuilder();
            var inString = false;
            var inComment = false;

            for (int i = 0; i < content.Length; i++) {
                var c = content[i];

                if (inComment) {
                    if (c == '\n' || c == '\r') {
                        inComment = false;
                    }
                    continue;
                }

                if (c == ';' && !inString && i + 1 < content.Length && content[i + 1] == ';') {
                    inComment = true;
                    i++; // Skip the second semicolon
                    continue;
                }

                if (c == '"' && !inComment) {
                    if (inString) {
                        current.Append(c);
                        tokens.Add(current.ToString());
                        current.Clear();
                        inString = false;
                    } else {
                        if (current.Length > 0) {
                            tokens.Add(current.ToString());
                            current.Clear();
                        }
                        current.Append(c);
                        inString = true;
                    }
                } else if (inString) {
                    current.Append(c);
                } else if (c == '(' || c == ')' || c == '[' || c == ']') {
                    if (current.Length > 0) {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    tokens.Add(c.ToString());
                } else if (char.IsWhiteSpace(c)) {
                    if (current.Length > 0) {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                } else {
                    current.Append(c);
                }
            }

            if (current.Length > 0) {
                tokens.Add(current.ToString());
            }

            return tokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        }

        private static SExpression ParseSExpression(List<string> tokens) {
            int index = 0;
            return ParseSExpressionInternal(tokens, ref index);
        }

        private static SExpression ParseSExpressionInternal(List<string> tokens, ref int index) {
            if (index >= tokens.Count) {
                throw new InvalidDataException("Unexpected end of tokens");
            }

            var token = tokens[index];
            
            if (token == "(") {
                index++;
                var items = new List<SExpression>();
                while (index < tokens.Count && tokens[index] != ")") {
                    items.Add(ParseSExpressionInternal(tokens, ref index));
                }
                if (index >= tokens.Count) {
                    throw new InvalidDataException("Missing closing parenthesis");
                }
                index++; // Skip ')'
                return new SExpression { Type = SExpressionType.List, Items = items };
            } else {
                index++;
                var value = token;
                if (value.StartsWith("\"") && value.EndsWith("\"")) {
                    value = value.Substring(1, value.Length - 2);
                }
                return new SExpression { Type = SExpressionType.Atom, Value = value };
            }
        }

        private static LayoutDto ParseLayout(SExpression expr) {
            if (expr.Type != SExpressionType.List || expr.Items.Count < 3) {
                throw new InvalidDataException("Invalid layout format");
            }

            var layout = new LayoutDto();
            var graph = new LayoutGraphDto();

            for (int i = 1; i < expr.Items.Count; i++) {
                var item = expr.Items[i];
                if (item.Type != SExpressionType.List || item.Items.Count < 2) continue;

                var command = item.Items[0].Value;
                switch (command) {
                    case "version":
                        if (int.TryParse(item.Items[1].Value, out var version)) {
                            layout.Version = version;
                        }
                        break;
                    case "blocks":
                        ParseBlocks(item, graph);
                        break;
                }
            }

            layout.Graph = graph;
            return layout;
        }

        private static void ParseBlocks(SExpression blocksExpr, LayoutGraphDto graph) {
            for (int i = 1; i < blocksExpr.Items.Count; i++) {
                var item = blocksExpr.Items[i];
                if (item.Type != SExpressionType.List || item.Items.Count < 2) continue;

                var command = item.Items[0].Value;
                switch (command) {
                    case "view-offset":
                        if (item.Items.Count >= 3) {
                            if (float.TryParse(item.Items[1].Value, out var vx)) graph.VX = vx;
                            if (float.TryParse(item.Items[2].Value, out var vy)) graph.VY = vy;
                        }
                        break;
                    case "block":
                        var node = ParseBlock(item);
                        if (node != null) graph.Nodes.Add(node);
                        break;
                    case "wires":
                        ParseWires(item, graph);
                        break;
                }
            }
        }

        private static NodeDto? ParseBlock(SExpression blockExpr) {
            if (blockExpr.Items.Count < 2) return null;
            
            var node = new NodeDto {
                Id = blockExpr.Items[1].Value,
                X = 0, Y = 0, W = 190, H = 98
            };

            for (int i = 2; i < blockExpr.Items.Count; i++) {
                var item = blockExpr.Items[i];
                if (item.Type != SExpressionType.List || item.Items.Count < 2) continue;

                var command = item.Items[0].Value;
                switch (command) {
                    case "title":
                        node.Title = item.Items[1].Value;
                        break;
                    case "position":
                        if (item.Items.Count >= 3) {
                            if (float.TryParse(item.Items[1].Value, out var x)) node.X = x;
                            if (float.TryParse(item.Items[2].Value, out var y)) node.Y = y;
                        }
                        break;
                    case "size":
                        if (item.Items.Count >= 3) {
                            if (float.TryParse(item.Items[1].Value, out var w)) node.W = w;
                            if (float.TryParse(item.Items[2].Value, out var h)) node.H = h;
                        }
                        break;
                    case "type":
                        node.Type = item.Items[1].Value;
                        break;
                    case "is-permanent":
                        node.IsPermanent = item.Items[1].Value == "true";
                        break;
                    case "const-value":
                        node.ConstValue = item.Items[1].Value;
                        break;
                    case "marshaller-output-type":
                        node.MarshallerOutputType = item.Items[1].Value;
                        break;
                    case "is-proxy":
                        node.IsProxy = item.Items[1].Value == "true";
                        break;
                    case "proxy-is-inlet":
                        node.ProxyIsInlet = item.Items[1].Value == "true";
                        break;
                    case "proxy-index":
                        if (int.TryParse(item.Items[1].Value, out var proxyIndex)) {
                            node.ProxyIndex = proxyIndex;
                        }
                        break;
                    case "inputs":
                        node.Inputs = ParsePorts(item);
                        break;
                    case "outputs":
                        node.Outputs = ParsePorts(item);
                        break;
                    case "inner":
                        var innerGraph = new LayoutGraphDto();
                        ParseBlocks(item, innerGraph);
                        node.Inner = innerGraph;
                        break;
                }
            }

            return node;
        }

        private static List<PortDef> ParsePorts(SExpression portsExpr) {
            var ports = new List<PortDef>();
            
            for (int i = 1; i < portsExpr.Items.Count; i++) {
                var item = portsExpr.Items[i];
                if (item.Type != SExpressionType.List || item.Items.Count < 1) continue;

                var port = new PortDef {
                    Name = item.Items[0].Value,
                    BitLength = 1,
                    Width = 108f
                };

                for (int j = 1; j < item.Items.Count; j++) {
                    var part = item.Items[j];
                    if (part.Type == SExpressionType.Atom && part.Value.StartsWith("[") && part.Value.EndsWith("]")) {
                        var bitLengthStr = part.Value.Substring(1, part.Value.Length - 2);
                        if (int.TryParse(bitLengthStr, out var bitLength)) {
                            port.BitLength = bitLength;
                        }
                    } else if (part.Type == SExpressionType.List && part.Items.Count >= 2) {
                        var subCommand = part.Items[0].Value;
                        switch (subCommand) {
                            case "user-type":
                                port.UserTypeName = part.Items[1].Value;
                                break;
                            case "width":
                                if (float.TryParse(part.Items[1].Value, out var width)) {
                                    port.Width = width;
                                }
                                break;
                        }
                    }
                }

                ports.Add(port);
            }

            return ports;
        }

        private static void ParseWires(SExpression wiresExpr, LayoutGraphDto graph) {
            for (int i = 1; i < wiresExpr.Items.Count; i++) {
                var item = wiresExpr.Items[i];
                if (item.Type != SExpressionType.List || item.Items.Count != 3) continue;
                if (item.Items[1].Value != "->") continue;

                var fromParts = item.Items[0].Value.Split('.');
                var toParts = item.Items[2].Value.Split('.');
                
                if (fromParts.Length == 2 && toParts.Length == 2) {
                    var edge = new EdgeDto {
                        From = new PortRefDto { NodeId = fromParts[0], Side = "Output", Port = fromParts[1] },
                        To = new PortRefDto { NodeId = toParts[0], Side = "Input", Port = toParts[1] }
                    };
                    graph.Edges.Add(edge);
                }
            }
        }
    }

    public enum SExpressionType {
        Atom,
        List
    }

    public class SExpression {
        public SExpressionType Type { get; set; }
        public string Value { get; set; } = "";
        public List<SExpression> Items { get; set; } = new();
    }
}