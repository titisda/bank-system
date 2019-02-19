﻿namespace BankSystem.Web.Controllers
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using AutoMapper;
    using Common;
    using Infrastructure.Handlers;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Rendering;
    using Models.MoneyTransfer;
    using Services.Interfaces;
    using Services.Models.BankAccount;
    using Services.Models.MoneyTransfer;

    [Authorize]
    public class MoneyTransfersController : BaseController
    {
        private readonly IBankConfigurationService bankConfigurationHelper;
        private readonly IMoneyTransferService moneyTransferService;
        private readonly IBankAccountService bankAccountService;
        private readonly IUserService userService;

        public MoneyTransfersController(
            IMoneyTransferService moneyTransferService,
            IBankAccountService bankAccountService,
            IUserService userService,
            IBankConfigurationService bankConfigurationHelper)
        {
            this.moneyTransferService = moneyTransferService;
            this.bankAccountService = bankAccountService;
            this.userService = userService;
            this.bankConfigurationHelper = bankConfigurationHelper;
        }

        public async Task<IActionResult> Create()
        {
            var userAccounts = await this.GetAllUserAccountsAsync();
            if (!userAccounts.Any())
            {
                this.ShowErrorMessage(NotificationMessages.NoAccountsError);
                return this.RedirectToHome();
            }

            var model = new MoneyTransferCreateBindingModel
            {
                UserAccounts = userAccounts,
            };

            return this.View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Create(MoneyTransferCreateBindingModel model)
        {
            if (!this.TryValidateModel(model))
            {
                model.UserAccounts = await this.GetAllUserAccountsAsync();
                return this.View(model);
            }

            // Contact central api
            var handler = new CustomDelegatingHandler(this.bankConfigurationHelper.AppId, this.bankConfigurationHelper.ApiKey);
            var client = HttpClientFactory.Create(handler);
            var response = await client.PostAsJsonAsync($"{GlobalConstants.CentralApiBaseAddress}api/ReceiveTransactions", model);
            if (!response.IsSuccessStatusCode)
            {
                this.ShowErrorMessage(NotificationMessages.TryAgainLaterError);
                return this.RedirectToHome();
            }

            // If we got this far the payment process was successful and we can store the data in database
            var serviceModel = Mapper.Map<MoneyTransferCreateServiceModel>(model);
            var isSuccessful = await this.moneyTransferService.CreateMoneyTransferAsync(serviceModel);
            if (!isSuccessful)
            {
                this.ShowErrorMessage(NotificationMessages.TryAgainLaterError);
            }

            this.ShowSuccessMessage(NotificationMessages.SuccessfulMoneyTransfer);
            return this.RedirectToHome();
        }

        private async Task<IEnumerable<SelectListItem>> GetAllUserAccountsAsync()
        {
            var userId = await this.userService.GetUserIdByUsernameAsync(this.User.Identity.Name);
            var userAccounts = await this.bankAccountService
                .GetAllUserAccountsAsync<BankAccountIndexServiceModel>(userId);

            return userAccounts
                .Select(a => new SelectListItem { Text = a.Name, Value = a.Id })
                .ToArray();
        }
    }
}
