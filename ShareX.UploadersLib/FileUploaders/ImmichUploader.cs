#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using FluentFTP.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Asn1.Pkcs;
using ShareX.HelpersLib;
using ShareX.UploadersLib.Properties;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using System.Xml.Linq;
using static System.Windows.Forms.Design.AxImporter;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace ShareX.UploadersLib.ImageUploaders
{
    public class ImmichUploaderServiceVideo : FileUploaderService
    {
        public override FileDestination EnumValue { get; } = FileDestination.Immich;

        //public override Icon ServiceIcon => Resources.Immich;

        public override bool CheckConfig(UploadersConfig config) => true;

        public override GenericUploader CreateUploader(UploadersConfig config, TaskReferenceHelper taskInfo)
        {
            return new ImmichUploader()
            {
                APIKey = config.ImmichAPIKey,
                UploadURL = config.uploadURL,
                DeviceId = config.deviceId
            };
        }

        public override TabPage GetUploadersConfigTabPage(UploadersConfigForm form) => form.tpVgyme;
    }

    public sealed class ImmichUploaderVideo : ImageUploader
    {
        public string APIKey { get; set; }
        public string UploadURL { get; set; }
        public string DeviceId { get; set; }

        public enum SharedLinkType
        {
            ALBUM,
            INDIVIDUAL
        }

        public override UploadResult Upload(Stream stream, string fileName)
        {
            UploadURL = UploadURL.TrimEnd('/');
            using HttpClient httpClient = new HttpClient
            {
                BaseAddress = new Uri(UploadURL + "/api/")
            };

            httpClient.DefaultRequestHeaders.Add("x-api-key", APIKey);

            UploadResult uploadResult = new UploadResult();
            var streamContent = new StreamContent(stream);
            using var uploadContent = new MultipartFormDataContent();

            uploadContent.Add(streamContent, "assetData", fileName);
            uploadContent.Add(new StringContent(fileName), "filename");
            uploadContent.Add(new StringContent(DeviceId), "deviceAssetId");
            uploadContent.Add(new StringContent(DeviceId), "deviceId");
            uploadContent.Add(new StringContent(DateTime.Now.ToString("O")), "fileCreatedAt");
            uploadContent.Add(new StringContent(DateTime.Now.ToString("O")), "fileModifiedAt");


            using HttpRequestMessage uploadRequest = new(System.Net.Http.HttpMethod.Post, UploadURL + "/api/" + "assets")
            {
                Content = uploadContent
            };

            using HttpResponseMessage result = httpClient.Send(uploadRequest);

            // Immich does not automatically share the link after uploading
            if (result != null)
            {
                string jsonResponse = result.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ImmichResponse response = JsonConvert.DeserializeObject<ImmichResponse>(jsonResponse);

                var sharePayload = new
                {
                    type = SharedLinkType.INDIVIDUAL,
                    assetIds = new[] { response.id }
                };
                var options = new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter() },
                };

                string jsonString = JsonSerializer.Serialize(sharePayload, options);
                using var shareContent = new StringContent(jsonString, System.Text.Encoding.UTF8, "application/json");

                using HttpRequestMessage shareRequest = new(System.Net.Http.HttpMethod.Post, UploadURL + "/api/" + "shared-links")
                {
                    Content = shareContent
                };
                using HttpResponseMessage shareResult = httpClient.Send(shareRequest);
                string uploadJsonResponse = shareResult.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                SharedLinkResponse shareResponse = JsonConvert.DeserializeObject<SharedLinkResponse>(uploadJsonResponse);

                // why couldnt you just give me the url :(
                using HttpRequestMessage getURLRequest = new(System.Net.Http.HttpMethod.Get, UploadURL + "/api/" + "server/config");
                using HttpResponseMessage URLResult = httpClient.Send(getURLRequest);
                string URLJsonResponse = URLResult.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ServerConfig URLResponse = JsonConvert.DeserializeObject<ServerConfig>(URLJsonResponse);

                // now let's finally fucking construct the link
                string link = URLResponse.externalDomain + "/share/photo/" + shareResponse.key + "/" + response.id + "/original";
                UploadResult finalResult = new UploadResult();
                finalResult.URL = link;
                finalResult.IsSuccess = true;
                return finalResult;
            }

            return uploadResult; // empty
        }

        public class ImmichResponse
        {
            public string id { get; set; }

        }

        public class SharedLinkResponse
        {
            public string key { get; set; }
            public string slug { get; set; }
        }

        public class ServerConfig
        {
            public string externalDomain { get; set; }

        }

    }

    
}