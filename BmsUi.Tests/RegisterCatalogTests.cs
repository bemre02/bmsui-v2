using System.Globalization;
using BmsUi.Protocol;
using Xunit;

// Formatting is asserted against InvariantCulture so the tests do not depend on the
// machine's language; the UI itself formats with CurrentCulture.

public class RegisterCatalogTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void All_CoversEveryReadableIndex_AndNothingElse()
    {
        var indices = RegisterCatalog.All.Select(d => (int)d.Index).ToList();

        // 0..40 and 44..49 — 41/42/43 are shadowed by commands, >=50 is dropped
        Assert.Equal(47, indices.Count);
        Assert.Equal(indices.Count, indices.Distinct().Count());
        Assert.DoesNotContain(41, indices);
        Assert.DoesNotContain(42, indices);
        Assert.DoesNotContain(43, indices);
        Assert.All(RegisterCatalog.All, d => Assert.True(HvProtocol.IsValidRegister(d.Index)));
    }

    [Fact]
    public void Describe_KnownRegister_CarriesNameUnitAndScale()
    {
        var d = RegisterCatalog.Describe(Reg.PackVoltage);
        Assert.Equal("PACK_VOLTAGE", d.Name);
        Assert.Equal("V", d.Unit);
        Assert.Equal(100.0, d.Scale);
        Assert.False(d.Signed);
        Assert.True(d.IsKnown);
    }

    [Fact]
    public void Describe_UnnamedRegister_IsStillWellFormed()
    {
        var d = RegisterCatalog.Describe(19);
        Assert.False(d.IsKnown);
        Assert.Equal(RegisterGroup.Unnamed, d.Group);
        Assert.Equal(1.0, d.Scale);
    }

    [Fact]
    public void FormatValue_SignedRegisters_ShowNegatives()
    {
        // PACK_CURRENT: signed, x10 -> -37.5 A
        Assert.Equal("-37.5 A", RegisterCatalog.FormatValue(Reg.PackCurrent, unchecked((ushort)-375), Culture));
        // CHARGE_OVER_CURRENT_TRESHOLD: firmware init is -500 -> -50.0 A
        Assert.Equal("-50.0 A", RegisterCatalog.FormatValue(34, unchecked((ushort)-500), Culture));
        // MIN_CELL_TEMP: signed, x100 -> -12.50 C
        Assert.Equal("-12.50 °C", RegisterCatalog.FormatValue(Reg.MinCellTemp, unchecked((ushort)-1250), Culture));
    }

    [Fact]
    public void FormatValue_UnsignedRegisters_UseTheirScale()
    {
        Assert.Equal("374.41 V", RegisterCatalog.FormatValue(Reg.PackVoltage, 37441, Culture));
        Assert.Equal("6.0 A", RegisterCatalog.FormatValue(4, 60, Culture));          // CHARGER_SET_CURRENT
        Assert.Equal("73.00 %", RegisterCatalog.FormatValue(Reg.EstimatedSoc, 7300, Culture));
        Assert.Equal("5 mV", RegisterCatalog.FormatValue(Reg.AllowedDisbalance, 5, Culture));
        Assert.Equal("100 ms", RegisterCatalog.FormatValue(36, 100, Culture));       // OVER_VOLTAGE_ERROR_DELAY
    }

    [Fact]
    public void FormatValue_BitMaskRegisters_ShowHexNotAScaledNumber()
    {
        Assert.Equal("0x0006", RegisterCatalog.FormatValue(Reg.Faults, 0x0006));
        Assert.Equal("0x0003", RegisterCatalog.FormatValue(Reg.Outputs, 0x0003));
    }

    [Fact]
    public void FormatRaw_IsAlwaysPlainDecimal_EvenForBitMasks()
    {
        // The hex form is FormatValue's job; printing it in both columns is duplication
        Assert.Equal("37441", RegisterCatalog.FormatRaw(Reg.PackVoltage, 37441, Culture));
        Assert.Equal("6", RegisterCatalog.FormatRaw(Reg.Faults, 6, Culture));
        Assert.Equal("0x0006", RegisterCatalog.FormatValue(Reg.Faults, 6, Culture));
    }

    [Fact]
    public void FormatNote_DecodesFaultBits()
    {
        string note = RegisterCatalog.FormatNote(Reg.Faults, (1 << 2) | (1 << 13));
        Assert.Contains("Cell overvoltage", note);
        Assert.Contains("Precharge timeout", note);
        Assert.Contains("ADBMS ref drift",
            RegisterCatalog.FormatNote(Reg.Faults, 1 << 15));
    }

    [Fact]
    public void FormatValue_DischargeOverCurrent_IsUnsigned()
    {
        // FW init DISCHARGE_OVER_CURRENT_TRESHOLD = 3500 → 350.0 A (unsigned)
        Assert.Equal("350.0 A", RegisterCatalog.FormatValue(35, 3500, Culture));
        Assert.False(RegisterCatalog.Describe(35).Signed);
    }

    [Fact]
    public void FormatNote_DecodesOutputsAndChargingState()
    {
        Assert.Contains("AIR", RegisterCatalog.FormatNote(Reg.Outputs, OutputBits.Air));
        // Firmware enum: 1 NO_CHARGER, 2 CHARGING, 3 BALANCING, 4 COMPLETED, 5 ERROR
        Assert.Contains("CHARGING", RegisterCatalog.FormatNote(2, 2));
    }

    [Fact]
    public void FormatNote_NoFaults_SaysSo()
        => Assert.Equal("none", RegisterCatalog.FormatNote(Reg.Faults, 0));

    [Fact]
    public void Groups_AreOrdered_StatusChargerPackConfigUnnamed()
    {
        var order = RegisterCatalog.All.Select(d => d.Group).ToList();
        var expected = order.OrderBy(g => (int)g).ToList();
        Assert.Equal(expected, order);
        Assert.Equal(RegisterGroup.Charger, RegisterCatalog.Describe(5).Group);
        Assert.Equal(RegisterGroup.Config, RegisterCatalog.Describe(33).Group);
    }
}
