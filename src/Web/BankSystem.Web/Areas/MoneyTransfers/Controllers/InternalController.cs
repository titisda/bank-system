namespace BankSystem.Web.Areas.MoneyTransfers.Controllers
{
    using AutoMapper;
    using Common;
    using Common.EmailSender.Interface;
    using Infrastructure.Filters;
    using Microsoft.AspNetCore.Mvc;
    using Models.Internal;
    using Services.Interfaces;
    using Services.Models.BankAccount;
    using Services.Models.MoneyTransfer;
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    public class InternalController : BaseMoneyTransferController
    {
        private readonly IMoneyTransferService moneyTransferService;
        private readonly IBankAccountService bankAccountService;
        private readonly IUserService userService;
        private readonly IEmailSender emailSender;

        public InternalController(
            IMoneyTransferService moneyTransferService,
            IBankAccountService bankAccountService,
            IUserService userService, 
            IEmailSender emailSender)
            : base(bankAccountService)
        {
            this.moneyTransferService = moneyTransferService;
            this.userService = userService;
            this.emailSender = emailSender;
            this.bankAccountService = bankAccountService;
        }

        public async Task<IActionResult> Create()
        {
            var userId = await this.userService.GetUserIdByUsernameAsync(this.User.Identity.Name);
            var userAccounts = await this.GetAllAccountsAsync(userId);

            if (!userAccounts.Any())
            {
                this.ShowErrorMessage(NotificationMessages.NoAccountsError);
                return this.RedirectToHome();
            }

            var model = new InternalMoneyTransferCreateBindingModel
            {
                OwnAccounts = userAccounts
            };

            return this.View(model);
        }

        [HttpPost]
        [EnsureOwnership]
        public async Task<IActionResult> Create(InternalMoneyTransferCreateBindingModel model)
        {
            var userId = await this.userService.GetUserIdByUsernameAsync(this.User.Identity.Name);

            if (!this.ModelState.IsValid)
            {
                model.OwnAccounts = await this.GetAllAccountsAsync(userId);
                return this.View(model);
            }

            var account =
                await this.bankAccountService.GetByIdAsync<BankAccountIndexServiceModel>(model.AccountId);
            if (string.Equals(account.UniqueId, model.DestinationBankAccountUniqueId, StringComparison.InvariantCulture))
            {
                this.ShowErrorMessage(NotificationMessages.SameAccountsError);
                model.OwnAccounts = await this.GetAllAccountsAsync(userId);
                return this.View(model);
            }
            if (account.Balance < model.Amount)
            {
                this.ShowErrorMessage(NotificationMessages.InsufficientFunds);
                model.OwnAccounts = await this.GetAllAccountsAsync(userId);
                return this.View(model);
            }

            var destinationAccount =
                await this.bankAccountService.GetByUniqueIdAsync<BankAccountConciseServiceModel>(
                    model.DestinationBankAccountUniqueId);
            if (destinationAccount == null || !string.Equals(
                    destinationAccount.UserFullName, account.UserFullName, StringComparison.InvariantCulture))
            {
                this.ShowErrorMessage(NotificationMessages.DestinationBankAccountDoesNotExist);
                model.OwnAccounts = await this.GetAllAccountsAsync(userId);
                return this.View(model);
            }

            var sourceServiceModel = Mapper.Map<MoneyTransferCreateServiceModel>(model);
            sourceServiceModel.Source = account.UniqueId;
            sourceServiceModel.Amount *= -1;
            sourceServiceModel.SenderName = account.UserFullName;
            sourceServiceModel.RecipientName = account.UserFullName;

            if (!await this.moneyTransferService.CreateMoneyTransferAsync(sourceServiceModel))
            {
                this.ShowErrorMessage(NotificationMessages.TryAgainLaterError);
                model.OwnAccounts = await this.GetAllAccountsAsync(userId);
                return this.View(model);
            }

            var destinationServiceModel = Mapper.Map<MoneyTransferCreateServiceModel>(model);
            destinationServiceModel.Source = account.UniqueId;
            destinationServiceModel.AccountId = destinationAccount.Id;
            destinationServiceModel.SenderName = account.UserFullName;
            destinationServiceModel.RecipientName = destinationAccount.UserFullName;

            if (!await this.moneyTransferService.CreateMoneyTransferAsync(destinationServiceModel))
            {
                this.ShowErrorMessage(NotificationMessages.TryAgainLaterError);
                model.OwnAccounts = await this.GetAllAccountsAsync(userId);
                return this.View(model);
            }

            this.ShowSuccessMessage(NotificationMessages.SuccessfulMoneyTransfer);
            return this.RedirectToHome();
        }
    }
}