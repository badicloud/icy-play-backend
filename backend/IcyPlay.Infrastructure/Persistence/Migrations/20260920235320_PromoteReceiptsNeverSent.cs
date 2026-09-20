using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IcyPlay.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Sends on the receipts that were uploaded and never submitted.
    ///
    /// Attaching a receipt and submitting it used to be two steps. Attaching
    /// stopped the hold's clock, so anything left between them held its hours
    /// for ever while sitting in a state no desk queue reads — the customer
    /// had paid and been told the venue was checking, and the venue had never
    /// been shown it.
    ///
    /// Attaching now submits, so the state is unreachable from here on. What
    /// is left is the rows the old code made, and they are stranded: nothing
    /// will move them, because nothing puts anything there any more. This
    /// moves them on to the queue they should have reached.
    /// </summary>
    public partial class PromoteReceiptsNeverSent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE BookingUpgradeRequests
                SET Status = 'AwaitingApproval',
                    UpdatedAt = SYSDATETIMEOFFSET()
                WHERE Status = 'AwaitingPayment'
                  AND ReceiptUrl IS NOT NULL;
                """);

            // The booking's own checkout had the same two steps and the same
            // gap. 1 is PendingPayment and 2 is PendingVerification.
            migrationBuilder.Sql(
                """
                UPDATE Bookings
                SET Status = 2,
                    UpdatedAt = SYSDATETIMEOFFSET()
                WHERE Status = 1
                  AND ReceiptUrl IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately nothing.
            //
            // Going back would mean picking out the rows this moved from the
            // ones that reached the queue honestly, and after the fact they
            // look identical. Stranding a paid customer again to undo a
            // migration is not a trade worth making, and the state it would
            // put them back into is one the code can no longer produce.
        }
    }
}
