﻿using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using Orchard.OpenId.Settings;
using Orchard.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using YesSql.Core.Services;

namespace Orchard.OpenId.Services
{
    public class OpenIdService : IOpenIdService
    {
        private readonly ISession _session;
        private readonly ISiteService _siteService;
        private readonly IMemoryCache _memoryCache;
        public OpenIdService(ISiteService siteService, IMemoryCache memoryCache, ISession session)
        {
            _siteService = siteService;
            _memoryCache = memoryCache;
            _session = session;
        }
        public async Task<OpenIdSettings> GetOpenIdSettingsAsync()
        {
            var siteSettings = await _siteService.GetSiteSettingsAsync();
            var result = siteSettings.As<OpenIdSettings>();

            if (result == null)
            {
                result = new OpenIdSettings();
            }
            return result;
        }
        public async Task UpdateOpenIdSettingsAsync(OpenIdSettings openIdSettings)
        {
            var siteSettings = await _session.QueryAsync<Orchard.Settings.SiteSettings>().FirstOrDefault();
            siteSettings.Properties[nameof(OpenIdSettings)] = JObject.FromObject(openIdSettings);
            _session.Save(siteSettings);
            _memoryCache.Set("Site", siteSettings);
        }
        public bool IsValidOpenIdSettings(OpenIdSettings settings, ModelStateDictionary modelState)
        {
            if (settings == null)
            {
                modelState.AddModelError("", "Settings are not stablished");
                return false;
            }

            if (settings.DefaultTokenFormat == OpenIdSettings.TokenFormat.JWT)
            {
                ValidateUrisSchema(new string[] { settings.Authority }, !settings.TestingModeEnabled, modelState, "Authority");
                ValidateUrisSchema(settings.Audience.Split(','), !settings.TestingModeEnabled, modelState, "Audience");
            }

            if (!settings.TestingModeEnabled)
            {
                if (settings.CertificateStoreName == null)
                {
                    modelState.AddModelError("CertificateStoreName", "A Certificate Store Name is required");
                }
                if (settings.CertificateStoreLocation == null)
                {
                    modelState.AddModelError("CertificateStoreLocation", "A Certificate Store Location is required");
                }
                if (string.IsNullOrWhiteSpace(settings.CertificateThumbPrint))
                {
                    modelState.AddModelError("CertificateThumbPrint", "A certificate is required when testing mode is disabled");
                }
            }
            return modelState.IsValid;
        }

        private static void ValidateUrisSchema(IEnumerable<string> uriStrings, bool onlyAllowHttps, ModelStateDictionary modelState, string modelStateKey)
        {
            foreach (var uriString in uriStrings.Select(a=> a.Trim()))
            {
                Uri uri;
                if (!Uri.TryCreate(uriString, UriKind.Absolute, out uri) || ((onlyAllowHttps || uri.Scheme!="http") && uri.Scheme!="https"))
                {
                    var message = "Invalid url.";
                    if (onlyAllowHttps)
                        message += " Non https urls are only allowed in testing mode";
                    modelState.AddModelError(modelStateKey, message);
                }
            }
        }

        public bool IsValidOpenIdSettings(OpenIdSettings settings)
        {
            var modelState = new ModelStateDictionary();
            return IsValidOpenIdSettings(settings, modelState);
        }

        public IEnumerable<CertificateInfo> GetAvailableCertificates()
        {
            foreach (StoreLocation storeLocation in Enum.GetValues(typeof(StoreLocation)))
            {
                foreach (StoreName storeName in Enum.GetValues(typeof(StoreName)))
                {
                    using (X509Store x509Store = new X509Store(storeName, storeLocation))
                    {
                        yield return new CertificateInfo()
                        {
                            StoreLocation = storeLocation,
                            StoreName = storeName
                        };

                        x509Store.Open(OpenFlags.ReadOnly);

                        X509Certificate2Collection col = x509Store.Certificates;
                        foreach (var cert in col)
                        {
                            if (!cert.Archived)
                            {
                                yield return new CertificateInfo()
                                {
                                    StoreLocation = storeLocation,
                                    StoreName = storeName,
                                    FriendlyName = cert.FriendlyName,
                                    Issuer = cert.Issuer,
                                    Subject = cert.Subject,
                                    NotBefore = cert.NotBefore,
                                    NotAfter = cert.NotAfter,
                                    ThumbPrint = cert.Thumbprint
                                };
                            }
                        }
                    }
                }
            }
        }
    }
}
