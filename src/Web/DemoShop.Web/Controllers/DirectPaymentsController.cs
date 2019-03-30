namespace DemoShop.Web.Controllers
{
    using System.Threading.Tasks;
    using Configuration;
    using DemoShop.Models;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;
    using PaymentHelpers;
    using Services.Interfaces;

    [Authorize]
    public class DirectPaymentsController : Controller
    {
        private const string ReturnPath = "DirectPayments/ReceiveConfirmation?data={0}";
        private readonly IOrdersService ordersService;

        private readonly DirectPaymentConfiguration directPaymentConfiguration;
        private readonly DestinationBankAccountConfiguration destinationBankAccountConfiguration;

        public DirectPaymentsController(IOrdersService ordersService,
            IOptions<DirectPaymentConfiguration> directPaymentConfigurationOptions,
            IOptions<DestinationBankAccountConfiguration> destinationBankAccountConfigurationOptions)
        {
            this.ordersService = ordersService;
            this.directPaymentConfiguration = directPaymentConfigurationOptions.Value;
            this.destinationBankAccountConfiguration = destinationBankAccountConfigurationOptions.Value;
        }

        public async Task<IActionResult> Pay(string id)
        {
            try
            {
                var order = await this.ordersService.GetByIdAsync(id);

                if (order == null ||
                    order.UserName != this.User.Identity.Name ||
                    order.PaymentStatus != PaymentStatus.Pending)
                {
                    return this.RedirectToAction("My", "Orders");
                }

                var paymentInfo = new
                {
                    Amount = order.ProductPrice,
                    Description = order.ProductName,
                    this.destinationBankAccountConfiguration.DestinationBankName,
                    this.destinationBankAccountConfiguration.DestinationBankCountry,
                    this.destinationBankAccountConfiguration.DestinationBankSwiftCode,
                    this.destinationBankAccountConfiguration.DestinationBankAccountUniqueId,
                    this.destinationBankAccountConfiguration.RecipientName,

                    // ! PaymentInfo can also contain custom properties
                    // ! that will be returned on payment completion

                    // ! OrderId is a custom property and is not required
                    OrderId = order.Id
                };

                // generate the returnUrl where the payment result will be received
                var returnUrl = this.directPaymentConfiguration.SiteUrl + ReturnPath;

                // generate signed payment request
                var paymentRequest = DirectPaymentsHelper.GeneratePaymentRequest(
                    paymentInfo, this.directPaymentConfiguration.SiteKey, returnUrl);

                var centralApiRedirectUrl = string.Format(
                    this.directPaymentConfiguration.CentralApiPaymentUrl,
                    paymentRequest);

                // redirect the user to the CentralApi for payment processing
                return this.Redirect(centralApiRedirectUrl);
            }
            catch
            {
                return this.RedirectToAction("My", "Orders");
            }
        }

        [AllowAnonymous]
        public async Task<IActionResult> ReceiveConfirmation(string data)
        {
            if (data == null)
            {
                return this.BadRequest();
            }

            dynamic paymentInfo = DirectPaymentsHelper.ProcessPaymentResult(
                data,
                this.directPaymentConfiguration.SiteKey,
                this.directPaymentConfiguration.CentralApiPublicKey);

            if (paymentInfo == null)
            {
                // if the returned PaymentInfo is null, it has not been parsed or verified successfully
                return this.BadRequest();
            }

            // extract the orderId from the PaymentInfo
            string orderId;

            try
            {
                orderId = paymentInfo.OrderId;
            }
            catch
            {
                return this.BadRequest();
            }

            if (orderId == null)
            {
                return this.BadRequest();
            }

            // find the order in the database
            var order = await this.ordersService.GetByIdAsync(orderId);

            // check if the order does not exist or the payment has already been completed 
            if (order == null || order.PaymentStatus != PaymentStatus.Pending)
            {
                return this.RedirectToAction("My", "Orders");
            }

            // mark the payment as completed
            await this.ordersService.SetPaymentStatus(orderId, PaymentStatus.Completed);

            return this.RedirectToAction("My", "Orders");
        }
    }
}