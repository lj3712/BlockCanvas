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
                    Type = n.Type.ToString(),
                    IsPermanent = n.IsPermanent,
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

        private static Graph FromDto(LayoutGraphDto dto, Graph? parent, Node? owner) {
            var g = new Graph { Parent = parent, Owner = owner };
            g.ViewOffset = new PointF(dto.VX, dto.VY);
            var idMap = new Dictionary<string, Node>();

            foreach (var nDto in dto.Nodes) {
                var nodeType = Enum.TryParse<NodeType>(nDto.Type, out var parsedType) ? parsedType : NodeType.Regular;
                var n = new Node(nDto.Title, new PointF(nDto.X, nDto.Y), createDefaultPorts: false, nodeType) {
                    Id = string.IsNullOrWhiteSpace(nDto.Id) ? Guid.NewGuid().ToString("N") : nDto.Id,
                    IsProxy = nDto.IsProxy,
                    ProxyIsInlet = nDto.ProxyIsInlet,
                    ProxyIndex = nDto.ProxyIndex,
                    IsPermanent = nDto.IsPermanent,
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