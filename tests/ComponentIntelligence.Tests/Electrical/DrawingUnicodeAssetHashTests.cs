using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ComponentIntelligence.Electrical.Drawing;

namespace ComponentIntelligence.Tests.Electrical;

public sealed class DrawingUnicodeAssetHashTests
{
    [Fact]
    public void PlanningHash_UsesUtf8CanonicalAssetPath_NotHtmlEscapedText()
    {
        var input = new DrawingPlanningInput { ProjectId = "P", Representations = [new DrawingRepresentationDecision
        {
            RepresentationId = "R", OwnerKind = DrawingRepresentationOwnerKind.Component, OwnerId = "I", Role = DrawingRepresentationRole.Schematic,
            Family = DrawingRepresentationFamily.ArchivedExact, AllowedRotations = [0], AssetPath = "archive/\u5716\u584a/a.dwg"
        }] };
        var json = DrawingPlanningJson.Serialize(input);
        Assert.Contains("archive/\u5716\u584a/a.dwg", json);
        var node = JsonNode.Parse(json)!.AsObject(); var hash = node["planningInputHash"]!.GetValue<string>(); node["planningInputHash"] = null;
        var canonical = node.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))), hash);
    }

    [Fact]
    public void DrawingPlanHash_UsesUtf8CanonicalIssueText_NotEscapedUnicode()
    {
        var plan = new DrawingPlanDocument
        {
            ProjectId = "P",
            SourcePlanningInputHash = new string('1', 64),
            SourcePagePlanHash = new string('2', 64),
            Issues =
            [
                new DrawingPlanIssue
                {
                    IssueId = "ISSUE:PAGE:P1:SPACE",
                    Severity = DrawingPlanningIssueSeverity.Warning,
                    Code = "DRAWING_SOFT_SPACING_CONSTRAINT",
                    Message = "頁面需要更密集的間距或續頁檢查。",
                    TargetKind = "DrawingPage",
                    TargetId = "P1"
                }
            ]
        };

        var json = DrawingPlanJson.Serialize(plan);
        Assert.Contains("頁面需要更密集的間距或續頁檢查。", json);
        var node = JsonNode.Parse(json)!.AsObject();
        var hash = node["drawingPlanHash"]!.GetValue<string>();
        node["drawingPlanHash"] = null;
        var canonical = node.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))), hash);
    }
}
