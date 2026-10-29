using System;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using EventTicketing.Api.Extensions;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EventTicketing.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/wallet")]
    [Authorize]
    public class WalletController : ControllerBase
    {
        private readonly IWalletService _walletService;

        public WalletController(IWalletService walletService)
        {
            _walletService = walletService;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var userId = User.GetUserId();
            var result = await _walletService.GetBalanceAsync(userId, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }

        [HttpPost("fund")]
        [EnableRateLimiting("wallet")]
        public async Task<IActionResult> Fund([FromBody] FundWalletRequest request, CancellationToken cancellationToken)
        {
            var userId = User.GetUserId();
            var result = await _walletService.FundAsync(userId, request.Amount, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
    }
}
