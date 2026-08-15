using ComponentIntelligence.Knowledge;
using Xunit;

namespace ComponentIntelligence.Tests.Knowledge;

public sealed class VendorPartIntakePromptTests
{
    [Fact]
    public void Build_EmbedsIdentityAndStableArchiveRules()
    {
        var prompt = VendorPartIntakePrompt.Build("ACME Motion", "CUSTOM-24V-001", "CMP-CUSTOM");

        Assert.Contains(VendorPartIntakePrompt.ContractVersion, prompt, StringComparison.Ordinal);
        Assert.Contains("ACME Motion", prompt, StringComparison.Ordinal);
        Assert.Contains("CUSTOM-24V-001", prompt, StringComparison.Ordinal);
        Assert.Contains("CMP-CUSTOM", prompt, StringComparison.Ordinal);
        Assert.Contains("Canonical Key", prompt, StringComparison.Ordinal);
        Assert.Contains("Unknown 必須保持 Unknown", prompt, StringComparison.Ordinal);
        Assert.Contains("NEEDS_PORT_MAPPING", prompt, StringComparison.Ordinal);
        Assert.Contains("Components / Documents / Ports / Pins / Specifications / Projects / BOM Items", prompt, StringComparison.Ordinal);
        Assert.Contains("component-intelligence-vendor-intake-v1", prompt, StringComparison.Ordinal);
        Assert.Contains("cable_or_adapter_mapping", prompt, StringComparison.Ordinal);
        Assert.Contains("Topology Ready", prompt, StringComparison.Ordinal);
        Assert.Contains("Category / Material Role", prompt, StringComparison.Ordinal);
        Assert.Contains("Cable Assembly", prompt, StringComparison.Ordinal);
        Assert.Contains("Connection Material", prompt, StringComparison.Ordinal);
        Assert.Contains("公司自製／內部 Layout 的電路板使用 PCB", prompt, StringComparison.Ordinal);
        Assert.Contains("無法確認角色時 category = Unknown", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DoesNotInventMissingIdentity()
    {
        var prompt = VendorPartIntakePrompt.Build();

        Assert.Contains("<UNKNOWN - ask me or derive only from explicit evidence>", prompt, StringComparison.Ordinal);
        Assert.Contains("NEEDS_IDENTITY", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Manufacturer / Vendor（製造商／供應商）：UNKNOWN_VENDOR", prompt, StringComparison.Ordinal);
    }
}
