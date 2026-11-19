using FHT.Access.Domain;

namespace FHT.Access.Tests;

public class AccessIdentificationTests
{
    [Fact]
    public void Maps_face_pin_manual_and_card_without_the_typed_code()
    {
        Assert.Equal("facial", AccessIdentification.MethodForSource("face"));
        Assert.Equal("pin", AccessIdentification.MethodForSource("pin"));
        Assert.Equal("manual", AccessIdentification.MethodForSource("manual"));
        Assert.Equal("card", AccessIdentification.MethodForSource("rfid"));
        Assert.Null(AccessIdentification.MethodForSource("import"));
    }
}
