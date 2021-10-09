namespace Terrasoft.Configuration.UsrCustomNamespace
{
    using System;
    using System.ServiceModel;
    using System.ServiceModel.Web;
    using System.ServiceModel.Activation;
    using Terrasoft.Core;
    using Terrasoft.Core.Configuration;
    using Terrasoft.Core.Entities;
    using Terrasoft.Web.Common;
    using System.Collections.Generic;
    using System.Globalization;
    using Newtonsoft.Json;
    using System.Net;
    using System.Text;
    using System.IO;

    public class CurrencyModel
    {
        public decimal inverseRate { get; set; }
        public string numericCode { get; set; }
    }



    [ServiceContract]
    [AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
    
    public class UsrCustomConfigurationService : BaseService
    {
       private string baseCurrencyAlphabeticCode = "";//usd или EUR
       
       private static void ShowMessage(string messageText)
        {
            var sender = "UpdatingCurrencyRates";
            MsgChannelUtilities.PostMessageToAll(sender, messageText);
        }

       
        [OperationContract]
        [WebInvoke(Method = "GET", RequestFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Wrapped,
        ResponseFormat = WebMessageFormat.Json)]
        
        public Dictionary<string, Guid> GetUpToDateCurrencyRates()
        {
            var baseCurrencyGuid =  (Guid)SysSettings.GetValue(UserConnection, "PrimaryCurrency");//получение id основной валюты из таблицы [SysSettings] по полю Code
            var currencyGuidsByNumericCode = new Dictionary<string, Guid>();//создаем новый словарь
            var esqCurrency = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "Currency");//подключение к таблице валюты
            var colCurrencyId = esqCurrency.AddColumn("Id");//добавление колонки для выборки
            var colCurrencyNumericCode = esqCurrency.AddColumn("Code");//добавление для выборки
            var colCurrencyShortName = esqCurrency.AddColumn("ShortName");//для выборки
            var entitiesCurrency = esqCurrency.GetEntityCollection(UserConnection);//сбор коллекции

            foreach (var entity in entitiesCurrency)//выборка из выше созданной коллекции
            {
                var currencyGuid = entity.GetTypedColumnValue<Guid>(colCurrencyId.Name);//получение айди валюты 
                if (currencyGuid == baseCurrencyGuid)//если айди валюты равно основной валюте то получаем ShortName
                {
                    baseCurrencyAlphabeticCode = entity.GetTypedColumnValue<string>(colCurrencyShortName.Name);
                    if (string.IsNullOrEmpty(baseCurrencyAlphabeticCode))//если пустое или нул то остановка функции
                    {
                        return null;
                    }
                }
                else
                {
                    var currencyNumericCode = entity.GetTypedColumnValue<string>(colCurrencyNumericCode.Name);//получаем Code
                    currencyGuidsByNumericCode.Add(currencyNumericCode, currencyGuid);//заполняем словарь 
                }
            }
           return  currencyGuidsByNumericCode;
        }
        

        [OperationContract]
        [WebInvoke(Method = "GET", RequestFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Wrapped,
        ResponseFormat = WebMessageFormat.Json)]
        
        public Dictionary<Guid, decimal> GetApi()
        {
        	var baseCurrencyGuid =  (Guid)SysSettings.GetValue(UserConnection, "PrimaryCurrency");
        	var currencyGuidsByNumericCode = GetUpToDateCurrencyRates();
        	
        	if (baseCurrencyAlphabeticCode == null)//если пустой то выход
            {
                return null;
            }
        	
        	
        	  var wrappedNewRates = new Dictionary<string, CurrencyModel>();//создаем новый словарь  
            var newRatesByCurrencyGuid = new Dictionary<Guid, decimal>();


            var requestAddress = $"http://www.floatrates.com/daily/{ baseCurrencyAlphabeticCode }.json"; //запрос к апи 
            var request = (HttpWebRequest)WebRequest.Create(requestAddress);
            request.Accept = "application/json";

            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var responseJson = "";
                    var encoding = Encoding.GetEncoding(response.CharacterSet);

                    using (var responseStream = response.GetResponseStream())
                    using (var reader = new StreamReader(responseStream, encoding))
                    {
                        responseJson = reader.ReadToEnd();
                    }
                    try
                    {
                        wrappedNewRates = JsonConvert.DeserializeObject<Dictionary<string, CurrencyModel>>(responseJson);//распаковка с джсон
                    }
                    catch 
                    {
                        return null;
                    }

                    foreach (var item in wrappedNewRates)//выборка с распаковки джсон
                    {
                        var numericCode = item.Value.numericCode; //дергаем валью numericCode
                        try
                        {
                            var currencyGuid = currencyGuidsByNumericCode[numericCode];//дергаем по ключу значение
                            var creatioRate = item.Value.inverseRate;//курс с апи 
                            newRatesByCurrencyGuid.Add(currencyGuid, creatioRate);//заполняем словарь новым курсом 
                        }
                        catch
                        {
                           
                        }
                    }
                   
                    newRatesByCurrencyGuid.Add(baseCurrencyGuid, decimal.Parse("1.0", CultureInfo.InvariantCulture));
                    return newRatesByCurrencyGuid;
                }
                else
                { 
                    return null;
                }
            }
        }
        
        

        [OperationContract]
        [WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Wrapped,
        ResponseFormat = WebMessageFormat.Json)]
        public void UpdateCurrencyRates()
        {
            var newRatesByCurrencyGuid = GetApi();//вызываем первый метод словаря
            if (newRatesByCurrencyGuid == null)//если пустой то выход
            {
                return;
            }

            var esqCurrencyRate = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "CurrencyRate");//таблица курсов
            esqCurrencyRate.AddAllSchemaColumns();
            var entitiesCurrencyRate = esqCurrencyRate.GetEntityCollection(UserConnection);//

            foreach (var entity in entitiesCurrencyRate)
            {
                var currencyGuid = entity.GetTypedColumnValue<Guid>("CurrencyId");
                try
                {
                    var newRate = newRatesByCurrencyGuid[currencyGuid];//берем по ключу значение
                    var newMantissa = CurrencyRateHelper.GetRateMantissa(newRate);
                    entity.SetColumnValue("Rate", newRate);
                    entity.SetColumnValue("RateMantissa", newMantissa);
                    entity.Save();
                }
                catch 
                {
                    return;
                }
            }
             ShowMessage("Курс валют обновлен");
        }
    }
}
