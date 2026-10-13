using ComponentIntelligence.Electrical.Domain;

namespace ComponentIntelligence.Electrical.Schematic;

public sealed record SchematicModuleEdit(string? Reference, double X, double Y, double Width, double Height, int Rotation, bool Locked, double? CadScale);
public sealed record SchematicWireEdit(string? Designation, int SizeMode, int Awg, double AreaMm2);

public static class SchematicInspectorEdit
{
    public static readonly IReadOnlyList<string> PinColors = new[] { "", "black", "red", "white", "orange", "pink", "grey", "brown", "blue", "green", "yellow", "violet" };
    public static ElectricalProject ApplyPinMetadata(ElectricalProject project, string symbolId, string pinId, string function, string color)
    {
        var symbol = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == symbolId) ?? throw new InvalidOperationException("模塊已不存在。");
        var owner = SchematicSymbolOwner.Resolve(project, symbol);
        if (symbol.Locked) throw new InvalidOperationException("請先解鎖模塊。");
        if (owner.Cable is not null) throw new InvalidOperationException("歸檔線材的功能由內部接法表管理，請使用線材編輯器。");
        if (!PinColors.Contains(color, StringComparer.Ordinal) || function.Length > 120 || function.Any(char.IsControl)) throw new InvalidOperationException("請使用有效功能與線色。");
        var pin = owner.Ports.SelectMany(p => p.Pins).SingleOrDefault(p => p.PinId == pinId) ?? throw new InvalidOperationException("腳位已不存在。");
        var name = function.Trim() + (color.Length == 0 ? "" : " / " + color);
        if (pin.Function == function.Trim() && pin.PinName == name) return project;
        var next = ComponentIntelligence.Electrical.Editing.EngineeringReviewService.Clone(project);
        var nextOwner = SchematicSymbolOwner.Resolve(next, symbol);
        var port = nextOwner.Ports.Single(p => p.Pins.Any(p => p.PinId == pinId));
        var updated = port.Pins.Single(p => p.PinId == pinId); updated.Function = function.Trim(); updated.PinName = name;
        for (var i = 0; i < next.Schematic!.Symbols.Count; i++) {
            var representation = next.Schematic.Symbols[i];
            if (representation.ComponentInstanceId != symbol.ComponentInstanceId) continue;
            next.Schematic.Symbols[i] = representation with { Anchors = representation.Anchors.Select(a => a.EndpointId == pinId ?
                a with { Label = port.Name + " / " + updated.PinNumber + " " + name } : a).ToList() };
        }
        SchematicAuthoringService.Validate(next);
        return next;
    }
    public static ElectricalProject ApplyModule(ElectricalProject project, string id, SchematicModuleEdit edit)
    {
        var symbol = project.Schematic?.Symbols.SingleOrDefault(s => s.SymbolId == id)
            ?? throw new InvalidOperationException("選取的模塊已不存在。");
        if (new[] { edit.X, edit.Y, edit.Width, edit.Height }.Any(v => !double.IsFinite(v)) || edit.Width <= 0 || edit.Height <= 0 ||
            edit.CadScale is double scale && (!double.IsFinite(scale) || scale is < .05 or > 20))
            throw new InvalidOperationException("位置與尺寸必須是有效數值；CAD 縮放需介於 5% 至 2000%。");
        if (edit.Rotation is not (0 or 90 or 180 or 270)) throw new InvalidOperationException("旋轉只接受 0、90、180、270 度。");
        var owner = SchematicSymbolOwner.Resolve(project, symbol);
        var body = SchematicPortPresentation.GenericBodyBounds(project.Schematic!, symbol, owner);
        var reference = string.IsNullOrWhiteSpace(edit.Reference) ? null : edit.Reference.Trim();
        var unchanged = reference == (string.IsNullOrWhiteSpace(owner.Reference) ? null : owner.Reference) &&
            symbol.Position == new SchematicPoint(edit.X, edit.Y) && symbol.Rotation == edit.Rotation && symbol.Locked == edit.Locked &&
            (symbol.Geometry is null ? Math.Abs(body.Width - edit.Width) < .001 && Math.Abs(body.Height - edit.Height) < .001 :
                edit.CadScale is null || Math.Abs(SchematicSymbolPresentation.CadDisplayScale(symbol) - edit.CadScale.Value) < .000001);
        if (unchanged) return project;
        var service = new SchematicAuthoringService();
        var next = project;
        if (symbol.Locked && edit.Locked) throw new InvalidOperationException("請先取消鎖定，再修改模塊。");
        if (symbol.Locked) next = service.SetLocked(next, id, false);
        if (reference != owner.Reference) next = service.SetSymbolDetails(next, id, reference, symbol.Anchors);
        if (symbol.Geometry is null && (Math.Abs(body.Width - edit.Width) > .001 || Math.Abs(body.Height - edit.Height) > .001))
        {
            next = service.ResizeGenericSymbol(next, id, edit.Width, edit.Height);
            var resized = next.Schematic!.Symbols.Single(s => s.SymbolId == id);
            var actual = SchematicPortPresentation.GenericBodyBounds(next.Schematic, resized, SchematicSymbolOwner.Resolve(next, resized));
            if (Math.Abs(actual.Width - edit.Width) > .001 || Math.Abs(actual.Height - edit.Height) > .001)
                throw new InvalidOperationException("尺寸太小，無法容納現有 Port／Pin；請增加尺寸。");
        }
        if (symbol.Geometry is not null && edit.CadScale is double cadScale && Math.Abs(SchematicSymbolPresentation.CadDisplayScale(symbol) - cadScale) > .000001)
            next = service.ScaleCadSymbol(next, id, cadScale);
        if (symbol.Position != new SchematicPoint(edit.X, edit.Y) || symbol.Rotation != edit.Rotation)
            next = service.TransformSymbol(next, id, new(edit.X, edit.Y), edit.Rotation);
        if (edit.Locked) next = service.SetLocked(next, id, true);
        var result = next.Schematic!.Symbols.Single(s => s.SymbolId == id);
        var bounds = SchematicPortPresentation.GenericBodyBounds(next.Schematic, result, SchematicSymbolOwner.Resolve(next, result));
        var page = next.Schematic.Pages.Single(p => p.PageId == result.PageId);
        if (bounds.X < page.Margin || bounds.Y < page.Margin || bounds.X + bounds.Width > page.Width - page.Margin || bounds.Y + bounds.Height > page.Height - page.Margin)
            throw new InvalidOperationException("模塊位置或尺寸超出有效圖面。");
        return next;
    }

    public static ElectricalProject ApplyWire(ElectricalProject project, string id, SchematicWireEdit edit)
    {
        var wire = project.Schematic?.Wires.SingleOrDefault(w => w.WireId == id)
            ?? throw new InvalidOperationException("選取的導線已不存在。");
        if (edit.SizeMode is < 0 or > 3) throw new InvalidOperationException("請選擇有效的線徑設定方式。");
        var service = new SchematicAuthoringService();
        var next = edit.SizeMode switch {
            1 => service.UseAutomaticWireSize(project, id),
            2 => service.SetWireAwg(project, id, edit.Awg),
            3 => service.SetWireArea(project, id, edit.AreaMm2),
            _ => project
        };
        return (edit.Designation ?? "").Trim() == (wire.Designation ?? "") ? next : service.SetWireDesignation(next, id, edit.Designation ?? "");
    }
}
