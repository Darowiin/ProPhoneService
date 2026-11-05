using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ProPhoneService.Application.Contracts;

namespace ProPhoneService.Api.Tests;

public class ServicePriceValidationTests
{
    [Theory]
    [InlineData("ru-RU", "99999999.99", true)]
    [InlineData("en-US", "99999999.99", true)]
    [InlineData("ru-RU", "100000000", false)]
    [InlineData("en-US", "-0.01", false)]
    public void Price_RespectsDatabaseRange(string culture, string price, bool expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var request = new ServiceUpsertRequest { Name = "Service", Price = decimal.Parse(price, CultureInfo.InvariantCulture) };

            var isValid = Validator.TryValidateObject(request, new ValidationContext(request), [], true);

            Assert.Equal(expected, isValid);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
