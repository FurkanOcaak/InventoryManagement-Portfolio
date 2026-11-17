namespace Business.Features.Products;

public static class BarcodeAssessment
{
    // Syntax/checksum only. A valid checksum does not prove GS1 allocation or ownership.
    public static string Assess(string barcode)
    {
        if (barcode.Length is not (8 or 12 or 13 or 14)
            || barcode.Any(character => character is < '0' or > '9'))
            return "InternalIdentifier";
        var sum = 0;
        for (var index = barcode.Length - 2; index >= 0; index--)
            sum += (barcode[index] - '0') * ((barcode.Length - 2 - index) % 2 == 0 ? 3 : 1);
        return barcode.Any(character => character != '0')
            && (10 - sum % 10) % 10 == barcode[^1] - '0'
            ? "GtinChecksumValid" : "GtinChecksumInvalid";
    }
}
