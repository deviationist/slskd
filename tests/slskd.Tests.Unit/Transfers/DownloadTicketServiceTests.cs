namespace slskd.Tests.Unit.Transfers
{
    using System;
    using System.Linq;
    using System.Threading;
    using slskd.Transfers;
    using Xunit;

    /// <summary>
    ///     Tickets authorizing one archive download.
    /// </summary>
    /// <remarks>
    ///     A ticket is the credential for a request that carries no other one, so every property here is load-bearing:
    ///     spent on first use, dead after a minute, and good only for what it was issued for.
    /// </remarks>
    public class DownloadTicketServiceTests
    {
        [Fact]
        public void A_Fresh_Ticket_Redeems_And_Returns_Its_Ids()
        {
            var service = new DownloadTicketService();
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };

            var (ticket, expiresAtUtc) = service.Issue("somebody", ids);

            Assert.Equal(TicketRedemption.Valid, service.Redeem(ticket, "somebody", out var redeemed));
            Assert.Equal(ids, redeemed);
            Assert.True(expiresAtUtc > DateTime.UtcNow);
        }

        [Fact]
        public void Ids_Come_Back_In_The_Order_They_Were_Issued_In()
        {
            // the order decides the order of the entries in the archive, and an archive that shuffles between two
            // identical requests is a worse thing to debug than one that does not
            var service = new DownloadTicketService();
            var ids = Enumerable.Range(0, 25).Select(_ => Guid.NewGuid()).ToArray();

            var (ticket, _) = service.Issue("somebody", ids);

            service.Redeem(ticket, "somebody", out var redeemed);

            Assert.Equal(ids, redeemed);
        }

        [Fact]
        public void A_Ticket_Can_Only_Be_Redeemed_Once()
        {
            var service = new DownloadTicketService();

            var (ticket, _) = service.Issue("somebody", [Guid.NewGuid()]);

            Assert.Equal(TicketRedemption.Valid, service.Redeem(ticket, "somebody", out _));
            Assert.Equal(TicketRedemption.Invalid, service.Redeem(ticket, "somebody", out var second));
            Assert.Null(second);
        }

        [Fact]
        public void A_Ticket_Is_Not_Valid_For_A_Different_Username()
        {
            // what stops a leaked ticket being replayed against somebody else's transfers
            var service = new DownloadTicketService();

            var (ticket, _) = service.Issue("somebody", [Guid.NewGuid()]);

            Assert.Equal(TicketRedemption.Invalid, service.Redeem(ticket, "somebody-else", out var ids));
            Assert.Null(ids);
        }

        [Fact]
        public void A_Ticket_Presented_For_The_Wrong_Username_Is_Still_Spent()
        {
            // a redemption is spent whatever the outcome, so a wrong guess cannot be followed by a right one
            var service = new DownloadTicketService();

            var (ticket, _) = service.Issue("somebody", [Guid.NewGuid()]);

            service.Redeem(ticket, "somebody-else", out _);

            Assert.Equal(TicketRedemption.Invalid, service.Redeem(ticket, "somebody", out _));
        }

        [Fact]
        public void An_Unknown_Ticket_Is_Invalid()
        {
            var service = new DownloadTicketService();

            Assert.Equal(TicketRedemption.Invalid, service.Redeem("not-a-ticket", "somebody", out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_Missing_Ticket_Is_Invalid(string ticket)
        {
            var service = new DownloadTicketService();

            Assert.Equal(TicketRedemption.Invalid, service.Redeem(ticket, "somebody", out _));
        }

        [Fact]
        public void An_Expired_Ticket_Says_So_Rather_Than_Reading_As_Unrecognised()
        {
            // the difference matters to whoever is looking at a failed download: "you were too slow" and "something
            // is wrong" call for different next steps
            var service = new DownloadTicketService(lifetime: TimeSpan.FromMilliseconds(20));

            var (ticket, _) = service.Issue("somebody", [Guid.NewGuid()]);

            Thread.Sleep(100);

            Assert.Equal(TicketRedemption.Expired, service.Redeem(ticket, "somebody", out var ids));
            Assert.Null(ids);
        }

        [Fact]
        public void Two_Tickets_Are_Never_The_Same()
        {
            var service = new DownloadTicketService();

            var tickets = Enumerable.Range(0, 100)
                .Select(_ => service.Issue("somebody", [Guid.NewGuid()]).Ticket)
                .ToArray();

            Assert.Equal(100, tickets.Distinct().Count());
            Assert.All(tickets, t => Assert.True(t.Length >= 43)); // 256 bits, base64url, unpadded
        }

        [Fact]
        public void A_Ticket_Is_Safe_To_Put_In_A_Url()
        {
            var service = new DownloadTicketService();

            var (ticket, _) = service.Issue("somebody", [Guid.NewGuid()]);

            Assert.Equal(ticket, Uri.EscapeDataString(ticket));
        }
    }
}
