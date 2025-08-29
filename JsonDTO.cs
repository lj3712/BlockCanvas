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
    // ===== JSON DTOs & converters =====

    public sealed class LayoutDto {
        [JsonPropertyName("version")] public int Version { get; set; } = 2;
        [JsonPropertyName("graph")] public LayoutGraphDto Graph { get; set; } = new();
    }

    public sealed class LayoutGraphDto {
        [JsonPropertyName("nodes")] public List<NodeDto> Nodes { get; set; } = new();
        [JsonPropertyName("edges")] public List<EdgeDto> Edges { get; set; } = new();

        // NEW
        [JsonPropertyName("vx")] public float VX { get; set; } = 0f;
        [JsonPropertyName("vy")] public float VY { get; set; } = 0f;
    }

    public sealed class NodeDto {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("x")] public float X { get; set; }
        [JsonPropertyName("y")] public float Y { get; set; }
        [JsonPropertyName("w")] public float W { get; set; }
        [JsonPropertyName("h")] public float H { get; set; }

        [JsonPropertyName("isProxy")] public bool IsProxy { get; set; }
        [JsonPropertyName("proxyIsInlet")] public bool ProxyIsInlet { get; set; }
        [JsonPropertyName("proxyIndex")] public int ProxyIndex { get; set; }

        [JsonPropertyName("type")] public string Type { get; set; } = "Regular";
        [JsonPropertyName("isPermanent")] public bool IsPermanent { get; set; } = false;
        [JsonPropertyName("constValue")] public string ConstValue { get; set; } = "0";

        [JsonPropertyName("inputs")] public List<PortDef> Inputs { get; set; } = new();
        [JsonPropertyName("outputs")] public List<PortDef> Outputs { get; set; } = new();

        [JsonPropertyName("inner")] public LayoutGraphDto? Inner { get; set; }
    }

    public sealed class EdgeDto {
        [JsonPropertyName("from")] public PortRefDto From { get; set; } = new();
        [JsonPropertyName("to")] public PortRefDto To { get; set; } = new();
    }

    public sealed class PortRefDto {
        [JsonPropertyName("node")] public string NodeId { get; set; } = "";
        [JsonPropertyName("side")] public string Side { get; set; } = "Output";
        [JsonPropertyName("port")] public string Port { get; set; } = "";
    }

    public sealed class PortDef {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("bitLength")] public int BitLength { get; set; } = 1;
        [JsonPropertyName("width")] public float Width { get; set; } = 108f;
        
        // Keep Type property for backward compatibility during transition
        [JsonPropertyName("type")] public string? Type { get; set; }
    }

    // Handles both new bitLength format and legacy type format
    public sealed class PortDefConverter : JsonConverter<PortDef> {
        public override PortDef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            if (reader.TokenType == JsonTokenType.String) {
                var name = reader.GetString() ?? "";
                return new PortDef { Name = name, BitLength = 1, Width = 108f };
            }
            if (reader.TokenType == JsonTokenType.StartObject) {
                string? name = null;
                int bitLength = 1;
                float width = 108f;
                string? legacyType = null;

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject) {
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;
                    var prop = reader.GetString();
                    reader.Read();
                    switch (prop) {
                        case "name": name = reader.GetString(); break;
                        case "bitLength": bitLength = reader.GetInt32(); break;
                        case "type": legacyType = reader.GetString(); break; // For backward compatibility
                        case "width": width = reader.GetSingle(); break;
                    }
                }
                
                // If we have legacy type but no bitLength, convert it
                if (legacyType != null && bitLength == 1) {
                    bitLength = TypeUtil.GetBitLength(legacyType);
                }
                
                return new PortDef { Name = name ?? "", BitLength = bitLength, Width = width };
            }
            throw new JsonException("Invalid PortDef");
        }

        public override void Write(Utf8JsonWriter writer, PortDef value, JsonSerializerOptions options) {
            writer.WriteStartObject();
            writer.WriteString("name", value.Name);
            writer.WriteNumber("bitLength", value.BitLength);
            writer.WriteNumber("width", value.Width);
            writer.WriteEndObject();
        }
    }
}
