namespace slskd.Tests.Unit.Transfers.Compare
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using slskd.Transfers.Compare.API;
    using Xunit;

    public class CompareControllerTests
    {
        [Fact]
        public async Task A_body_that_did_not_bind_is_a_400_in_fixed_words_not_a_500()
        {
            // the services are never reached: a body that did not bind is refused before anything is looked up
            var controller = new CompareController(null, null, null, null);

            foreach (var request in new[] { null, new CompareRequest { Ids = null } })
            {
                var result = Assert.IsType<BadRequestObjectResult>(await controller.CompareAsync(request));
                Assert.Equal("Send the downloads to compare as { \"ids\": [ ... ] }", result.Value);
            }
        }
    }
}
