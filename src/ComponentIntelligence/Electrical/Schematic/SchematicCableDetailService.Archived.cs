using ComponentIntelligence.Electrical.Domain;
using ComponentIntelligence.Electrical.Editing;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed partial class SchematicCableDetailService
{
    public ElectricalProject AddArchivedDetail(ElectricalProject project, string cableInstanceId, string? templatePageId = null,
        SchematicCableDetailBinding? layout = null)
    {
        var next = EngineeringReviewService.Clone(project);
        var cable = RequireArchived(next, cableInstanceId);
        next.Schematic ??= new();
        if (next.Schematic.Pages.Any(p => p.CableDetail?.CableInstanceId == cableInstanceId)) return next;
        var template = next.Schematic.Pages.SingleOrDefault(p => p.PageId == templatePageId);
        var page = (template ?? new SchematicPage { PageId = "", Title = "" }) with
        {
            PageId = "sheet-" + Guid.NewGuid().ToString("N"), Title = (cable.ReferenceDesignator ?? cable.DisplayName ?? "線材") + " / 製作明細",
            CableDetail = (layout ?? new(cableInstanceId, null)) with { CableInstanceId = cableInstanceId,
                CableAssemblyId = null, DetailId = "detail-" + Guid.NewGuid().ToString("N") },
            InlineCableDetails = []
        };
        next.Schematic.Pages.Add(page);
        Build(next, page); SchematicAuthoringService.Validate(next); return next;
    }

    public ElectricalProject PlaceArchivedDetail(ElectricalProject project, string cableInstanceId, string pageId,
        SchematicPoint tablePosition, SchematicPoint sketchPosition, SchematicCableDetailBinding? layout = null)
    {
        var next = EngineeringReviewService.Clone(project); RequireArchived(next, cableInstanceId);
        var page = next.Schematic?.Pages.Single(p => p.PageId == pageId) ?? throw new InvalidOperationException("頁面不存在。");
        if (page.CableDetail is not null) throw new InvalidOperationException("獨立明細頁請調整現有明細，不要新增另一份。");
        var binding = (layout ?? new(cableInstanceId, null)) with { CableInstanceId = cableInstanceId, CableAssemblyId = null, DetailId = "detail-" + Guid.NewGuid().ToString("N"),
            TablePosition = tablePosition, SketchPosition = sketchPosition };
        BuildArchived(next, page, binding); page.InlineCableDetails.Add(binding);
        SchematicAuthoringService.Validate(next); return next;
    }

    public ElectricalProject SetLayout(ElectricalProject project, string pageId, string detailId, SchematicCableDetailBinding layout)
    {
        var next = EngineeringReviewService.Clone(project);
        var index = next.Schematic!.Pages.FindIndex(p => p.PageId == pageId);
        var page = next.Schematic.Pages[index];
        var old = page.CableDetail?.DetailId == detailId ? page.CableDetail : page.InlineCableDetails.Single(b => b.DetailId == detailId);
        if (layout.CableInstanceId != old.CableInstanceId || layout.CableAssemblyId != old.CableAssemblyId || layout.DetailId != old.DetailId)
            throw new InvalidOperationException("調整明細位置不能更換來源線材。");
        BuildArchived(next, page, layout);
        if (page.CableDetail?.DetailId == detailId) next.Schematic.Pages[index] = page with { CableDetail = layout };
        else page.InlineCableDetails[page.InlineCableDetails.FindIndex(b => b.DetailId == detailId)] = layout;
        SchematicAuthoringService.Validate(next); return next;
    }

    public ElectricalProject RemoveInlineDetail(ElectricalProject project, string pageId, string detailId)
    {
        var next = EngineeringReviewService.Clone(project);
        var page = next.Schematic!.Pages.Single(p => p.PageId == pageId);
        if (page.InlineCableDetails.RemoveAll(b => b.DetailId == detailId) != 1) throw new InvalidOperationException("明細不存在。");
        SchematicAuthoringService.Validate(next); return next;
    }

    public ElectricalProject RemoveDetail(ElectricalProject project, string pageId, string detailId)
    {
        var page = project.Schematic!.Pages.Single(p => p.PageId == pageId);
        if (page.CableDetail?.DetailId != detailId) return RemoveInlineDetail(project, pageId, detailId);
        var next = EngineeringReviewService.Clone(project);
        var index = next.Schematic!.Pages.FindIndex(p => p.PageId == pageId);
        next.Schematic.Pages[index] = next.Schematic.Pages[index] with { CableDetail = null };
        SchematicAuthoringService.Validate(next); return next;
    }

    public SchematicCableDetailPresentation BuildArchived(ElectricalProject project, SchematicPage page, SchematicCableDetailBinding binding)
    {
        var cable = RequireArchived(project, binding.CableInstanceId); ValidateLayout(binding, page);
        var archived = cable.ArchivedCable!;
        var draft = new CableManufacturingEditorService().Prepare(cable);
        var pins = draft.Ends.SelectMany(p => p.Pins.Select(pin => (port: p, pin))).ToDictionary(x => x.pin.PinId);
        var diagnostics = new List<string>();
        if (!archived.MappingConfirmed) diagnostics.Add("CABLE_MAPPING_UNCONFIRMED");
        if (cable.ProvidedLengthMm is null) diagnostics.Add("CABLE_LENGTH_UNKNOWN");
        if (string.IsNullOrWhiteSpace(cable.ReferenceDesignator)) diagnostics.Add("CABLE_REFERENCE_UNKNOWN");
        if (string.IsNullOrWhiteSpace(cable.Specification)) diagnostics.Add("CABLE_SPECIFICATION_UNKNOWN");
        if (cable.CableConstructionType == CableConstructionType.Unknown) diagnostics.Add("CABLE_CONSTRUCTION_UNKNOWN");
        if (draft.Rows.Any(r => r.FromSourcePinId is null && r.FromUsage == CablePinUsage.Pending ||
            r.ToSourcePinId is null && r.ToUsage == CablePinUsage.Pending)) diagnostics.Add("CABLE_TABLE_INCOMPLETE");
        if (archived.ManufacturingGeometry is null) diagnostics.Add("CABLE_MANUFACTURING_CAD_MISSING");
        else diagnostics.AddRange(archived.ManufacturingGeometry.Diagnostics);
        var primitives = new List<SchematicCadPrimitive>();
        void Line(double x, double y, double ex, double ey) => primitives.Add(new() { Kind = "LINE", Start = new(x, y), End = new(ex, ey) });
        void Text(string text, double x, double y) => primitives.Add(new() { Kind = "TEXT", Start = new(x, y + 2.6), Text = text, TextHeight = 2.6 });
        var left = binding.TablePosition.X; var y = binding.TablePosition.Y; var right = left + binding.TableWidth;
        void Wrapped(string text)
        {
            foreach (var line in Wrap(text, Math.Max(1, (int)(binding.TableWidth / 2.7))))
            { Text(line, left + 2, y + 1); y += 5; }
        }
        Wrapped(SchematicSymbolPresentation.CableFieldValue(cable, CableTextField.Reference));
        Wrapped(SchematicSymbolPresentation.CableFieldValue(cable, CableTextField.LengthMm) + " / " +
            SchematicSymbolPresentation.CableFieldValue(cable, CableTextField.Specification));
        foreach (var end in draft.Ends)
            Wrapped(end.Name + ": " + (end.Connector is { } c
                ? $"{c.Family} {c.Coding} {end.Pins.Count}P {c.Gender}" : "接頭待確認"));
        if (diagnostics.Count > 0) Wrapped("草稿／未完成：" + string.Join(" / ", diagnostics));
        var columns = new[] { left, left + binding.TableWidth * .35, left + binding.TableWidth * .5, left + binding.TableWidth * .65, right };
        var top = y;
        Line(left, y, right, y);
        void Row(string[] cells)
        {
            var lines = cells.Select((value, i) => Wrap(value, Math.Max(1, (int)((columns[i + 1] - columns[i] - 4) / 2.7)))).ToArray();
            var height = lines.Max(v => v.Length) * 5 + 2;
            if (y + height > page.Height - page.Margin - 18)
                throw new InvalidOperationException("線材明細表超出圖面；請增加紙張或調整位置，不能省略接法列。");
            for (var col = 0; col < 4; col++)
                for (var row = 0; row < lines[col].Length; row++) Text(lines[col][row], columns[col] + 2, y + 1 + row * 5);
            y += height; Line(left, y, right, y);
        }
        Row(["P1 FUNCTION", "P1 PIN", "P2 PIN", "P2 FUNCTION"]);
        string Pin(string? id) => id is not null && pins.TryGetValue(id, out var p) ? p.port.Name + "/" + p.pin.PinNumber : "待填";
        string Function(string? value, CablePinUsage usage) => usage switch { CablePinUsage.Nc => "NC", CablePinUsage.Unused => "未使用", _ => value ?? "待確認" };
        foreach (var row in draft.Rows)
            Row([Function(row.FromFunction, row.FromUsage), Pin(row.FromSourcePinId), Pin(row.ToSourcePinId), Function(row.ToFunction, row.ToUsage)]);
        foreach (var x in columns) Line(x, top, x, y);
        if (y >= binding.SketchPosition.Y && left < binding.SketchPosition.X + binding.SketchWidth && right > binding.SketchPosition.X &&
            binding.TablePosition.Y < binding.SketchPosition.Y + binding.SketchHeight)
            throw new InvalidOperationException("接法表與製作示意圖重疊；請調整各自位置。");
        if (archived.ManufacturingGeometry is { } sketch)
        {
            var scale = Math.Min(binding.SketchWidth / sketch.Width, binding.SketchHeight / sketch.Height);
            var fields = archived.Template.TextBindings.ToDictionary(b => b.AttributeTag, b => b.Field, StringComparer.Ordinal);
            SchematicPoint Point(SchematicPoint p) => new(binding.SketchPosition.X + p.X * scale, binding.SketchPosition.Y + p.Y * scale);
            foreach (var p in sketch.Primitives)
            {
                var text = p.AttributeTag is { } tag && fields.TryGetValue(tag, out var field) ? SchematicSymbolPresentation.CableFieldValue(cable, field) : p.Text;
                if (p.Kind is "TEXT" or "MTEXT" && text is not null &&
                    (text.Length * p.TextHeight * p.TextWidthFactor > sketch.Width || p.TextHeight > sketch.Height))
                    throw new InvalidOperationException("製作示意 CAD 文字超出範圍；請擴大文字區或調整來源圖塊。");
                primitives.Add(p with { Start = Point(p.Start), End = p.End is null ? null : Point(p.End), Radius = p.Radius * scale,
                    Text = text, AttributeTag = null, TextHeight = p.TextHeight * scale, TextWidth = p.TextWidth * scale,
                    Contours = p.Contours.Select(c => (IReadOnlyList<SchematicPoint>)c.Select(Point).ToArray()).ToArray() });
            }
        }
        else Text("製作示意 CAD 待匯入", binding.SketchPosition.X, binding.SketchPosition.Y);
        return new(primitives, project.Connections.Where(c => c.CableInstanceId == cable.CableInstanceId).Select(c => c.ConnectionId).ToArray(),
            diagnostics.Distinct().ToArray());
    }

    public static void ValidateBindings(ElectricalProject project, SchematicPage page)
    {
        if (page.InlineCableDetails.Any(b => string.IsNullOrWhiteSpace(b.DetailId)) ||
            page.InlineCableDetails.Select(b => b.DetailId).Distinct().Count() != page.InlineCableDetails.Count)
            throw new InvalidOperationException("線材明細識別碼重複或缺失。");
        foreach (var binding in page.InlineCableDetails.Concat(page.CableDetail is null ? [] : new[] { page.CableDetail }))
        {
            if (!project.Cables.Any(c => c.CableInstanceId == binding.CableInstanceId))
                throw new InvalidOperationException("線材明細來源不存在。");
            if (project.Cables.Single(c => c.CableInstanceId == binding.CableInstanceId).ArchivedCable is not null) ValidateLayout(binding, page);
        }
    }

    private static void ValidateLayout(SchematicCableDetailBinding b, SchematicPage page)
    {
        var numbers = new[] { b.TablePosition.X, b.TablePosition.Y, b.SketchPosition.X, b.SketchPosition.Y, b.TableWidth, b.SketchWidth, b.SketchHeight };
        if (numbers.Any(v => !double.IsFinite(v)) || b.TableWidth < 60 || b.SketchWidth <= 0 || b.SketchHeight <= 0 ||
            b.TablePosition.X < page.Margin || b.TablePosition.Y < page.Margin || b.SketchPosition.X < page.Margin || b.SketchPosition.Y < page.Margin ||
            b.TablePosition.X + b.TableWidth > page.Width - page.Margin || b.SketchPosition.X + b.SketchWidth > page.Width - page.Margin ||
            b.SketchPosition.Y + b.SketchHeight > page.Height - page.Margin - 18)
            throw new InvalidOperationException("線材明細位置與尺寸超出有效圖面。");
    }

    private static CableInstance RequireArchived(ElectricalProject project, string id) =>
        project.Cables.SingleOrDefault(c => c.CableInstanceId == id && c.ArchivedCable is not null)
            ?? throw new InvalidOperationException("請選取已歸檔的線材實例。");
}
