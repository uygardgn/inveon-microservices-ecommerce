using AutoMapper;
using Inveon.Services.CouponAPI.DbContexts;
using Inveon.Services.CouponAPI.Models.Dto;
using Microsoft.EntityFrameworkCore;

namespace Inveon.Services.CouponAPI.Repository
{
    public class CouponRepository : ICouponRepository
    {
        private readonly ApplicationDbContext _db;
        protected IMapper _mapper;
        public CouponRepository(ApplicationDbContext db, IMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        public async Task<CouponDto> GetCouponByCode(string couponCode)
        {
            var couponFromDb = await _db.Coupons.FirstOrDefaultAsync(u => u.CouponCode == couponCode);
            if (couponFromDb == null)
            {
                CouponDto model = new CouponDto()
                {
                    CouponId = 0,
                    CouponCode = "0",
                    DiscountAmount = 0
                };
                return model;
            }
            return _mapper.Map<CouponDto>(couponFromDb);
        }
    }
}
