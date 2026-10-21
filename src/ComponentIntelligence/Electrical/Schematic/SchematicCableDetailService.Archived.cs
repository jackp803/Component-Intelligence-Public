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
            PageId = "sheet-" + Guid.NewGuid().ToString("N"),
            Title = (cable.ReferenceDesignator ?? cable.DisplayName ?? "線材") + " / 製作明細",
            CableDetail = (layout ?? new(cableInstanceId, null)) with
            {
                CableInstanceId = cableInstanceId,
                CableAssemblyId = null,
                DetailId = "detail-" + Guid.NewGuid().ToString("N")
            },
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
        var binding = (layout ?? new(cableInstanceId, null)) with
        {
            CableInstanceId = cableInstanceId,
            CableAssemblyId = null,
            DetailId = "detail-" + Guid.NewGuid().ToString("N"),
            TablePosition = tablePosition,
            SketchPosition = sketchPosition
        };
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

    public ElectricalProject RemovePart(ElectricalProject project, string pageId, string detailId, SchematicCableDetailPart part)
    {
        var page = project.Schematic!.Pages.Single(p => p.PageId == pageId);
        var binding = page.CableDetail?.DetailId == detailId ? page.CableDetail :
            page.InlineCableDetails.Single(b => b.DetailId == detailId);
        var layout = part == SchematicCableDetailPart.Table ? binding with { TableVisible = false } : binding with { SketchVisible = false };
        return !layout.TableVisible && !layout.SketchVisible ? RemoveDetail(project, pageId, detailId) : SetLayout(project, pageId, detailId, layout);
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
        var mapped = CableWiringEditorService.Mapping(draft).SelectMany(p => new[] { p.FromSourcePinId, p.ToSourcePinId }).ToHashSet(StringComparer.Ordinal);
        var declared = draft.Rows.SelectMany(CableManufacturingCells.All).Where(c => c.SourcePinId is not null && c.Usage != CablePinUsage.Pending)
            .Select(c => c.SourcePinId!).ToHashSet(StringComparer.Ordinal);
        if (pins.Keys.Any(id => !mapped.Contains(id) && !declared.Contains(id)) ||
            draft.Rows.Any(r => CableManufacturingCells.All(r).All(c => c.SourcePinId is null))) diagnostics.Add("CABLE_TABLE_INCOMPLETE");
        if (archived.ManufacturingGeometry is null) diagnostics.Add("CABLE_MANUFACTURING_CAD_MISSING");
        else diagnostics.AddRange(archived.ManufacturingGeometry.Diagnostics);
        var primitives = new List<SchematicCadPrimitive>();
        var bounds = new Dictionary<SchematicCableDetailPart, SchematicGridBounds>();
        var sketchView = binding.SketchVisible ? BuildSketch(cable, binding) : default;
        if (binding.SketchVisible) ValidateSketchBounds(sketchView.Bounds!, page);
        var unit = binding.TableScale;
        void Line(double x, double y, double ex, double ey) => primitives.Add(new() { Kind = "LINE", Start = new(x, y), End = new(ex, ey) });
        void Text(string text, double x, double y) => primitives.Add(new() { Kind = "TEXT", Start = new(x, y + 2.6 * unit), Text = text, TextHeight = 2.6 * unit });
        var left = binding.TablePosition.X; var y = binding.TablePosition.Y; var right = left + binding.TableWidth;
        if (binding.TableVisible)
        {
            void Wrapped(string text)
            {
                foreach (var line in Wrap(text, Math.Max(1, (int)(binding.TableWidth / (2.7 * unit)))))
                { Text(line, left + 2 * unit, y + unit); y += 5 * unit; }
            }
            Wrapped(SchematicSymbolPresentation.CableFieldValue(cable, CableTextField.Reference));
            Wrapped(SchematicSymbolPresentation.CableFieldValue(cable, CableTextField.LengthMm) + " / " +
                SchematicSymbolPresentation.CableFieldValue(cable, CableTextField.Specification));
            foreach (var end in draft.Ends)
                Wrapped(end.Name + ": " + (end.Connector is { } c
                    ? $"{c.Family} {c.Coding} {end.Pins.Count}P {c.Gender}" : "接頭待確認"));
            if (!string.IsNullOrWhiteSpace(cable.Notes))
                foreach (var note in cable.Notes.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                    Wrapped(note);
            if (draft.InventoryOnly) { Wrapped("Pin 清單：同列不表示導通"); Wrapped("接法以接線圖為準"); }
            var fields = draft.Ends.SelectMany(port => port.PortId == draft.FromSourcePortId
                ? new[] { (port.PortId, Header: port.Name + " FUNCTION", Function: true), (port.PortId, Header: port.Name + " PIN", Function: false) }
                : new[] { (port.PortId, Header: port.Name + " PIN", Function: false), (port.PortId, Header: port.Name + " FUNCTION", Function: true) }).ToArray();
            var weight = fields.Sum(f => f.Function ? 7d / 3 : 1);
            var minimumWidth = (4 + 2.7) * unit * weight;
            if (binding.TableWidth + .001 < minimumWidth)
                throw new InvalidOperationException($"線材接法表太窄；目前 Port 數與文字比例需要至少 {Math.Ceiling(minimumWidth)} mm 寬，請加寬表格或調小文字比例。");
            var columns = new List<double> { left };
            foreach (var field in fields) columns.Add(columns[^1] + binding.TableWidth * (field.Function ? 7d / 3 : 1) / weight);
            var top = y;
            Line(left, y, right, y);
            void Row(string[] cells)
            {
                var lines = cells.Select((value, i) => Wrap(value, Math.Max(1, (int)((columns[i + 1] - columns[i] - 4 * unit) / (2.7 * unit))))).ToArray();
                var height = (lines.Max(v => v.Length) * 5 + 2) * unit;
                if (y + height > page.Height - page.Margin - 18)
                    throw new InvalidOperationException("線材明細表超出圖面；請增加紙張或調整位置，不能省略接法列。");
                for (var col = 0; col < cells.Length; col++)
                    for (var row = 0; row < lines[col].Length; row++) Text(lines[col][row], columns[col] + 2 * unit, y + unit + row * 5 * unit);
                y += height; Line(left, y, right, y);
            }
            Row(fields.Select(f => f.Header).ToArray());
            string Pin(string? id) => id is not null && pins.TryGetValue(id, out var p) ? p.port.Name + "/" + p.pin.PinNumber : "待填";
            string Function(string? value, CablePinUsage usage) => usage switch { CablePinUsage.Nc => "NC", CablePinUsage.Unused => "未使用", _ => value ?? "待確認" };
            foreach (var row in CableManufacturingEditorService.SortRows(draft))
                Row(fields.Select(f => { var cell = CableManufacturingCells.Get(draft, row, f.PortId);
                    return f.Function ? Function(cell.Function, cell.Usage) : Pin(cell.SourcePinId); }).ToArray());
            foreach (var x in columns) Line(x, top, x, y);
            bounds.Add(SchematicCableDetailPart.Table, new(left, binding.TablePosition.Y, binding.TableWidth, y - binding.TablePosition.Y));
            if (binding.SketchVisible && y > sketchView.Bounds!.Y && left < sketchView.Bounds.X + sketchView.Bounds.Width && right > sketchView.Bounds.X &&
                binding.TablePosition.Y < sketchView.Bounds.Y + sketchView.Bounds.Height)
                throw new InvalidOperationException("接法表與製作示意圖重疊；請調整各自位置。");
        }
        if (binding.SketchVisible)
        {
            bounds.Add(SchematicCableDetailPart.Sketch, sketchView.Bounds!);
            if (archived.ManufacturingGeometry is not null) primitives.AddRange(sketchView.Primitives!);
            else Text("製作示意 CAD 待匯入", binding.SketchPosition.X, binding.SketchPosition.Y);
        }
        return new(primitives, project.Connections.Where(c => c.CableInstanceId == cable.CableInstanceId).Select(c => c.ConnectionId).ToArray(),
            diagnostics.Distinct().ToArray())
        { PartBounds = bounds };
    }

    public static void ValidateEditedLayouts(ElectricalProject before, ElectricalProject after, string cableId)
    {
        if (before.Schematic is null || after.Schematic is null) return;
        var service = new SchematicCableDetailService();
        foreach (var page in before.Schematic.Pages)
            foreach (var binding in page.InlineCableDetails.Concat(page.CableDetail is null ? [] : new[] { page.CableDetail })
                .Where(b => b.CableInstanceId == cableId))
            {
                // Existing invalid drafts must remain editable and savable for recovery.
                try { service.BuildArchived(before, page, binding); }
                catch (InvalidOperationException) { continue; }
                service.BuildArchived(after, after.Schematic.Pages.Single(p => p.PageId == page.PageId), binding);
            }
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
            var cable = project.Cables.Single(c => c.CableInstanceId == binding.CableInstanceId);
            if (cable.ArchivedCable is not null)
            {
                ValidateLayout(binding, page);
                if (binding.SketchVisible) ValidateSketchBounds(BuildSketch(cable, binding).Bounds, page);
            }
        }
    }

    private static void ValidateLayout(SchematicCableDetailBinding b, SchematicPage page)
    {
        var numbers = new[] { b.TablePosition.X, b.TablePosition.Y, b.SketchPosition.X, b.SketchPosition.Y, b.TableWidth, b.SketchWidth, b.SketchHeight, b.TableScale };
        if (numbers.Any(v => !double.IsFinite(v)) || b.TableWidth < 60 || b.SketchWidth <= 0 || b.SketchHeight <= 0 || b.TableScale < .2 || b.TableScale > 5 ||
            b.TableVisible && (b.TablePosition.X < page.Margin || b.TablePosition.Y < page.Margin || b.TablePosition.X + b.TableWidth > page.Width - page.Margin) ||
            b.SketchVisible && (b.SketchPosition.X < page.Margin || b.SketchPosition.Y < page.Margin))
            throw new InvalidOperationException("線材明細位置與尺寸超出有效圖面。");
    }

    public static (IReadOnlyList<SchematicCadPrimitive> Primitives, SchematicGridBounds Bounds) BuildSketch(CableInstance cable,
        SchematicCableDetailBinding binding, bool includeInstanceText = true)
    {
        if (cable.ArchivedCable!.ManufacturingGeometry is not { } sketch)
            return ([], new(binding.SketchPosition.X, binding.SketchPosition.Y, binding.SketchWidth, binding.SketchHeight));
        var fields = cable.ArchivedCable.Template.TextBindings.ToDictionary(b => b.AttributeTag, b => b.Field, StringComparer.Ordinal);
        var resolved = sketch.Primitives.Select(p => p with { Text = p.AttributeTag is { } tag && fields.TryGetValue(tag, out var field)
            ? !includeInstanceText ? "" :
                SchematicSymbolPresentation.CableFieldValue(cable, field) : p.Text, AttributeTag = null }).ToArray();
        var primitives = FitSketch(resolved, binding, out var bounds);
        return (primitives, bounds);
    }

    private static void ValidateSketchBounds(SchematicGridBounds bounds, SchematicPage page)
    {
        if (bounds.X + bounds.Width > page.Width - page.Margin + .000001 ||
            bounds.Y + bounds.Height > page.Height - page.Margin - 18 + .000001)
            throw new InvalidOperationException("線材明細位置與尺寸超出有效圖面。");
    }

    private static CableInstance RequireArchived(ElectricalProject project, string id) =>
        project.Cables.SingleOrDefault(c => c.CableInstanceId == id && c.ArchivedCable is not null)
            ?? throw new InvalidOperationException("請選取已歸檔的線材實例。");
}
