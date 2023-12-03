using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Iyzipay.Request;
using Iyzipay.Model;
using Iyzipay;
using Inveon.Services.ShoppingCartAPI.Messages;
using Inveon.Services.ShoppingCartAPI.RabbitMQSender;
using Inveon.Services.ShoppingCartAPI.Repository;
using Inveon.Services.ShoppingCartAPI.Models.Dto;

namespace Inveon.Service.ShoppingCartAPI.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("api/cartc")]
    public class CartAPICheckOutController : ControllerBase
    {
        private readonly ICartRepository _cartRepository;
        private readonly ICouponRepository _couponRepository;
        // private readonly IMessageBus _messageBus;
        protected ResponseDto _response;
        private readonly IRabbitMQCartMessageSender _rabbitMQCartMessageSender;
        // IMessageBus messageBus,
        public CartAPICheckOutController(ICartRepository cartRepository,
            ICouponRepository couponRepository, IRabbitMQCartMessageSender rabbitMQCartMessageSender)
        {
            _cartRepository = cartRepository;
            _couponRepository = couponRepository;
            _rabbitMQCartMessageSender = rabbitMQCartMessageSender;
            //_messageBus = messageBus;
            this._response = new ResponseDto();
        }

        [HttpPost]
        [Authorize]
        public async Task<object> Checkout([FromBody] CheckoutHeaderDto checkoutHeader)
        {
            try
            {
                CartDto cartDto = await _cartRepository.GetCartByUserId(checkoutHeader.UserId);
                if (cartDto == null)
                {
                    return BadRequest();
                }

                if (!string.IsNullOrEmpty(checkoutHeader.CouponCode))
                {
                    CouponDto coupon = await _couponRepository.GetCoupon(checkoutHeader.CouponCode);
                    if (checkoutHeader.DiscountTotal != coupon.DiscountAmount)
                    {
                        _response.IsSuccess = false;
                        _response.ErrorMessages = new List<string>() { "Coupon Price has changed, please confirm" };
                        _response.DisplayMessage = "Coupon Price has changed, please confirm";
                        return _response;
                    }
                }

                checkoutHeader.CartDetails = cartDto.CartDetails;
                //logic to add message to process order.
                // await _messageBus.PublishMessage(checkoutHeader, "checkoutqueue");

                ////rabbitMQ

                Payment payment = OdemeIslemi(checkoutHeader);
                if (payment.Status != "success")
                {
                    _response.IsSuccess = false;
                    _response.ErrorMessages = new List<string> { payment.ErrorMessage };
                    return _response;
                }
                _rabbitMQCartMessageSender.SendMessage(checkoutHeader, "checkoutqueue");
                await _cartRepository.ClearCart(checkoutHeader.UserId);
            }
            catch (Exception ex)
            {
                _response.IsSuccess = false;
                _response.ErrorMessages = new List<string>() { ex.ToString() };
            }
            return _response;
        }

        public Payment OdemeIslemi(CheckoutHeaderDto checkoutHeaderDto)
        {
            CartDto cartDto = _cartRepository.GetCartByUserIdNonAsync(checkoutHeaderDto.UserId);

            Options options = new Options();

            double discountRate = 1;

            options.ApiKey = "sandbox-YL3HKTd220GqA8hFU7amJ9KYQysAeFkk";
            options.SecretKey = "5AJpm92muYYB37B87WeUAHpbtqS2WwHX";
            options.BaseUrl = "https://sandbox-api.iyzipay.com";

            CreatePaymentRequest request = new CreatePaymentRequest();
            request.Locale = Locale.TR.ToString();
            request.ConversationId = new Random().Next(1111, 9999).ToString();
            request.Price = checkoutHeaderDto.OrderTotal.ToString();
            request.PaidPrice = checkoutHeaderDto.DiscountTotal.ToString();

            request.Price = checkoutHeaderDto.OrderTotal.ToString();
            request.PaidPrice = (checkoutHeaderDto.OrderTotal - checkoutHeaderDto.DiscountTotal).ToString();
            request.Currency = Currency.TRY.ToString();
            request.Installment = 1;
            request.BasketId = checkoutHeaderDto.CartHeaderId.ToString();
            request.PaymentChannel = PaymentChannel.WEB.ToString();
            request.PaymentGroup = PaymentGroup.PRODUCT.ToString();

            PaymentCard paymentCard = new PaymentCard();
            paymentCard.CardHolderName = checkoutHeaderDto.FirstName + " " + checkoutHeaderDto.LastName;
            paymentCard.CardNumber = checkoutHeaderDto.CardNumber;
            paymentCard.ExpireMonth = checkoutHeaderDto.ExpiryMonth;
            paymentCard.ExpireYear = checkoutHeaderDto.ExpiryYear;
            paymentCard.Cvc = checkoutHeaderDto.CVV;
            paymentCard.RegisterCard = 0;
            paymentCard.CardAlias = "Inveon";
            request.PaymentCard = paymentCard;

            Buyer buyer = new Buyer();
            buyer.Id = checkoutHeaderDto.UserId;
            buyer.Name = checkoutHeaderDto.FirstName;
            buyer.Surname = checkoutHeaderDto.LastName;
            buyer.GsmNumber = checkoutHeaderDto.Phone;
            buyer.Email = checkoutHeaderDto.Email;
            buyer.IdentityNumber = "74300864791";
            buyer.LastLoginDate = "2015-10-05 12:43:35";
            buyer.RegistrationDate = "2013-04-21 15:12:09";
            buyer.RegistrationAddress = "Akyazı Mah. Akyazı Konakları";
            buyer.Ip = "85.34.78.112";
            buyer.City = "Ordu";
            buyer.Country = "Turkey";
            buyer.ZipCode = "52200";
            request.Buyer = buyer;

            Address shippingAddress = new Address();
            shippingAddress.ContactName = checkoutHeaderDto.FirstName + " " + checkoutHeaderDto.LastName;
            shippingAddress.City = "Ordu";
            shippingAddress.Country = "Turkey";
            shippingAddress.Description = "Akyazı Mah. Akyazı Konakları";
            shippingAddress.ZipCode = "5200";
            request.ShippingAddress = shippingAddress;

            Address billingAddress = new Address();
            billingAddress.ContactName = checkoutHeaderDto.FirstName + " " + checkoutHeaderDto.LastName;
            billingAddress.City = "Ordu";
            billingAddress.Country = "Turkey";
            billingAddress.Description = "Akyazı Mah. Akyazı Konakları";
            billingAddress.ZipCode = "5200";
            request.BillingAddress = billingAddress;

            if (!string.IsNullOrEmpty(checkoutHeaderDto.CouponCode))
            {
                discountRate = 1.0 - checkoutHeaderDto.DiscountTotal / (checkoutHeaderDto.DiscountTotal + checkoutHeaderDto.OrderTotal);
            }

            List<BasketItem> basketItems = new List<BasketItem>();

            foreach (var item in checkoutHeaderDto.CartDetails)
            {
                BasketItem basketItem = new BasketItem();
                basketItem.Id = item.ProductId.ToString();
                basketItem.Name = item.Product.Name;
                basketItem.Category1 = item.Product.CategoryName;
                basketItem.ItemType = BasketItemType.PHYSICAL.ToString();

                double itemAmount = item.Product.Price * item.Count * discountRate;
                itemAmount = Math.Round(itemAmount, 2);
                basketItem.Price = (itemAmount).ToString();
                basketItems.Add(basketItem);
            }

            request.BasketItems = basketItems;

            return Payment.Create(request, options);
        }
    }
}
