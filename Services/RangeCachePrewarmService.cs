using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;
using MediaInfoKeeper.Options;

namespace MediaInfoKeeper.Services
{
    internal sealed class RangeCachePrewarmService
    {
        private readonly IHttpClient httpClient;
        private readonly ILogger logger;
        private readonly Func<PluginConfiguration> getOptions;

        public RangeCachePrewarmService(
            IHttpClient httpClient,
            ILogger logger,
            Func<PluginConfiguration> getOptions)
        {
            this.httpClient = httpClient;
            this.logger = logger;
            this.getOptions = getOptions;
        }

        public void TriggerAfterMediaInfoAvailable(
            BaseItem item,
            string source,
            IEnumerable<MediaSourceInfo> mediaSources)
        {
            var options = this.getOptions?.Invoke()?.MediaInfo;
            if (options?.EnableRangeCachePrewarm != true || item == null)
            {
                return;
            }

            var itemId = item.Id.ToString();
            var mediaSourceIds = (mediaSources ?? Enumerable.Empty<MediaSourceInfo>())
                .Select(mediaSource => mediaSource?.Id?.Trim())
                .Where(mediaSourceId => !string.IsNullOrWhiteSpace(mediaSourceId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (mediaSourceIds.Count == 0)
            {
                this.logger.Debug(
                    "{0} Range Cache 预热跳过: 无 MediaSourceId item={1}",
                    source,
                    FormatItemForLog(item));
                return;
            }

            foreach (var mediaSourceId in mediaSourceIds)
            {
                if (!RangeCachePrewarmRequest.TryCreate(
                        options.RangeCachePrewarmEndpoint,
                        options.RangeCachePrewarmSecret,
                        itemId,
                        mediaSourceId,
                        out var request))
                {
                    this.logger.Warn(
                        "{0} Range Cache 预热跳过: 配置不完整或无效 item={1}",
                        source,
                        FormatItemForLog(item));
                    return;
                }

                _ = Task.Run(() => PostPrewarmAsync(request, item, source, CancellationToken.None));
            }
        }

        private async Task PostPrewarmAsync(
            RangeCachePrewarmRequest request,
            BaseItem item,
            string source,
            CancellationToken cancellationToken)
        {
            if (this.httpClient == null)
            {
                this.logger.Warn(
                    "{0} Range Cache 预热跳过: IHttpClient 不可用 item={1}",
                    source,
                    FormatItemForLog(item));
                return;
            }

            try
            {
                var requestOptions = new HttpRequestOptions
                {
                    Url = request.Url,
                    CancellationToken = cancellationToken,
                    AcceptHeader = "application/json",
                    RequestContent = request.BodyJson.AsMemory(),
                    RequestContentType = "application/json",
                    UserAgent = "MediaInfoKeeper",
                    EnableDefaultUserAgent = false,
                    TimeoutMs = 3000,
                    ThrowOnErrorResponse = false,
                    LogRequest = false,
                    LogResponse = false,
                    LogErrors = false
                };
                requestOptions.RequestHeaders["X-Range-Cache-Prewarm-Key"] = request.Secret;

                using var response = await this.httpClient
                    .SendAsync(requestOptions, "POST")
                    .ConfigureAwait(false);
                var statusCode = (int)response.StatusCode;
                if (statusCode >= 200 && statusCode < 300)
                {
                    this.logger.Info(
                        "{0} Range Cache 预热已触发: item={1}",
                        source,
                        FormatItemForLog(item));
                    return;
                }

                this.logger.Warn(
                    "{0} Range Cache 预热触发失败: status={1} item={2}",
                    source,
                    statusCode,
                    FormatItemForLog(item));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                this.logger.Warn(
                    "{0} Range Cache 预热触发异常: {1} item={2}",
                    source,
                    ex.Message,
                    FormatItemForLog(item));
                this.logger.Debug(ex.StackTrace);
            }
        }

        private static string FormatItemForLog(BaseItem item)
        {
            return item?.FileName ?? item?.Path ?? item?.Name ?? item?.Id.ToString() ?? "unknown";
        }
    }
}
