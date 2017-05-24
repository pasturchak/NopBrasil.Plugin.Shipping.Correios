﻿using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Services.Directory;
using Nop.Services.Shipping;
using System;
using System.Text;
using System.Linq;
using Nop.Core.Caching;
using System.ServiceModel.Channels;
using System.ServiceModel;

namespace NopBrasil.Plugin.Shipping.Correios.Service
{
    public class CorreiosService : ICorreiosService
    {
        //colocar as unidades de medida e moeda utilizadas como configuração
        private const string MEASURE_WEIGHT_SYSTEM_KEYWORD = "kg";
        private const string MEASURE_DIMENSION_SYSTEM_KEYWORD = "centimeter";
        private const string CURRENCY_CODE = "BRL";
        //colocar o tamanho/peso mínimo/máximo permitido dos produtos como configuração

        //colocar cache nas pesquisas de medidas e peso
        private readonly ICacheManager _cacheManager;
        private readonly IMeasureService _measureService;
        private readonly IShippingService _shippingService;
        private readonly CorreiosSettings _correiosSettings;
        private readonly ICurrencyService _currencyService;
        private readonly CurrencySettings _currencySettings;

        public CorreiosService(ICacheManager cacheManager, IMeasureService measureService, IShippingService shippingService, CorreiosSettings correiosSettings,
            ICurrencyService currencyService, CurrencySettings currencySettings)
        {
            this._cacheManager = cacheManager;
            this._measureService = measureService;
            this._shippingService = shippingService;
            this._correiosSettings = correiosSettings;
            this._currencyService = currencyService;
            this._currencySettings = currencySettings;
        }

        public WSCorreiosCalcPrecoPrazo.cResultado RequestCorreios(GetShippingOptionRequest getShippingOptionRequest)
        {
            Binding binding = new BasicHttpBinding();
            binding.Name = "CalcPrecoPrazoWSSoap";

            decimal length, width, height;
            GetDimensions(getShippingOptionRequest, out width, out length, out height);

            decimal valuePackage = getShippingOptionRequest.Items.Sum(item => item.ShoppingCartItem.Product.Price);

            EndpointAddress endpointAddress = new EndpointAddress(_correiosSettings.Url);

            WSCorreiosCalcPrecoPrazo.CalcPrecoPrazoWSSoap wsCorreios = new WSCorreiosCalcPrecoPrazo.CalcPrecoPrazoWSSoapClient(binding, endpointAddress);
            return wsCorreios.CalcPrecoPrazo(_correiosSettings.CompanyCode, _correiosSettings.Password, GetSelectecServices(_correiosSettings), _correiosSettings.PostalCodeFrom,
                getShippingOptionRequest.ShippingAddress.ZipPostalCode, GetWheight(getShippingOptionRequest).ToString(), 1, length, height, width, 0, "N", valuePackage, "N");
        }

        private int GetWheight(GetShippingOptionRequest shippingOptionRequest)
        {
            var usedMeasureWeight = _measureService.GetMeasureWeightBySystemKeyword(MEASURE_WEIGHT_SYSTEM_KEYWORD);
            if (usedMeasureWeight == null)
                throw new NopException(string.Format("Correios shipping service. Could not load \"{0}\" measure weight", MEASURE_WEIGHT_SYSTEM_KEYWORD));

            int weight = Convert.ToInt32(Math.Ceiling(_measureService.ConvertFromPrimaryMeasureWeight(_shippingService.GetTotalWeight(shippingOptionRequest), usedMeasureWeight)));
            if (weight < 1)
                weight = 1;

            return weight;
        }

        private void GetDimensions(GetShippingOptionRequest shippingOptionRequest, out decimal width, out decimal length, out decimal height)
        {
            var usedMeasureDimension = _measureService.GetMeasureDimensionBySystemKeyword(MEASURE_DIMENSION_SYSTEM_KEYWORD);
            if (usedMeasureDimension == null)
                throw new NopException(string.Format("Correios shipping service. Could not load \"{0}\" measure dimension", MEASURE_DIMENSION_SYSTEM_KEYWORD));

            _shippingService.GetDimensions(shippingOptionRequest.Items, out width, out length, out height);

            length = _measureService.ConvertFromPrimaryMeasureDimension(length, usedMeasureDimension);
            if (length < 16)
                length = 16;

            height = _measureService.ConvertFromPrimaryMeasureDimension(height, usedMeasureDimension);
            if (height < 2)
                height = 2;

            width = _measureService.ConvertFromPrimaryMeasureDimension(width, usedMeasureDimension);
            if (width < 11)
                width = 11;
        }

        public decimal GetConvertedRate(decimal rate)
        {
            var usedCurrency = _currencyService.GetCurrencyByCode(CURRENCY_CODE);
            if (usedCurrency == null)
                throw new NopException(string.Format("Correios shipping service. Could not load \"{0}\" currency", CURRENCY_CODE));

            if (usedCurrency.CurrencyCode == _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId).CurrencyCode)
                return rate;
            else
                return _currencyService.ConvertToPrimaryStoreCurrency(rate, usedCurrency); //testar se não deve ser ConvertFromPrimaryStoreCurrency
        }

        private string GetSelectecServices(CorreiosSettings correioSettings)
        {
            StringBuilder sb = new StringBuilder();
            correioSettings.ServicesOffered.Split(':').ToList().ForEach(service => sb.Append(service?.Remove(0, 1).Replace(']', ',')));
            return sb.ToString().Remove(sb.ToString().Length - 1, 1);
        }
    }
}
