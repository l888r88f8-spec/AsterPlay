using System.Net;

namespace AsterPlay.Services;

public static class UserError
{
    public static bool IsAuthenticationFailure(Exception exception)
    {
        var http = FindException<HttpRequestException>(exception);
        return http?.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    public static string GetMessage(Exception exception, string? operation = null)
    {
        var current = Unwrap(exception);

        if (current is TaskCanceledException or TimeoutException)
        {
            return string.IsNullOrWhiteSpace(operation)
                ? "请求 Emby 服务器超时，请检查网络连接后重试。"
                : $"{operation}超时，请检查网络连接后重试。";
        }

        if (current is HttpRequestException http)
        {
            if (http.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return string.Equals(operation, "登录", StringComparison.Ordinal)
                    ? "登录失败：用户名或密码错误，或服务器拒绝了登录请求。"
                    : "登录状态已失效或 Access Token 无效，请重新登录。";
            }

            if (http.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
            {
                return string.IsNullOrWhiteSpace(operation)
                    ? "Emby 服务器响应超时，请稍后重试。"
                    : $"{operation}超时，请稍后重试。";
            }

            if (http.StatusCode == HttpStatusCode.NotFound)
            {
                return string.Equals(operation, "播放", StringComparison.Ordinal)
                    ? "媒体不存在、已被移动，或当前账号无权访问该媒体。"
                    : "请求的媒体或 Emby 资源不存在，可能已被移动或删除。";
            }

            if (http.Message.Contains("PlaybackInfo", StringComparison.OrdinalIgnoreCase))
            {
                return "无法从 Emby 获取播放信息。请检查媒体源是否仍可访问；如果需要转码，也请检查服务端转码配置。";
            }

            if (http.StatusCode is not null && (int)http.StatusCode.Value >= 500)
            {
                return $"Emby 服务器暂时不可用（HTTP {(int)http.StatusCode.Value}），请稍后重试。";
            }

            if (http.StatusCode is null)
            {
                return "无法连接 Emby 服务器。请检查服务器地址、网络连接、防火墙或 HTTPS 证书后重试。";
            }

            return $"Emby 请求失败（HTTP {(int)http.StatusCode.Value} {http.StatusCode.Value}）。";
        }

        if (current is DllNotFoundException)
            return "播放器运行库缺失。请重新运行发布构建，确认 libmpv-2.dll 及其依赖已包含在 AsterPlay 目录中。";

        if (current is BadImageFormatException)
            return "播放器运行库架构不匹配或文件已损坏。请使用 AsterPlay Windows x64 发布包重新安装。";

        if (current.Message.Contains("mpv_create", StringComparison.OrdinalIgnoreCase) ||
            current.Message.Contains("mpv_initialize", StringComparison.OrdinalIgnoreCase))
        {
            return "libmpv 初始化失败。请检查发布包完整性和显卡驱动；详细信息已写入 playback.log。";
        }

        if (current.Message.Contains("TranscodingUrl", StringComparison.OrdinalIgnoreCase))
            return "Emby 已选择转码，但没有返回有效的转码地址。请检查服务端转码配置和媒体源状态。";

        if (current.Message.Contains("No playable next episode", StringComparison.OrdinalIgnoreCase))
            return "当前剧集没有可播放的下一集。请从季度列表中选择具体集数。";

        return string.IsNullOrWhiteSpace(current.Message)
            ? "操作失败，请稍后重试。"
            : current.Message;
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is AggregateException aggregate &&
               aggregate.InnerExceptions.Count == 1)
        {
            exception = aggregate.InnerExceptions[0];
        }

        return exception;
    }

    private static TException? FindException<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException match)
                return match;
        }

        return null;
    }
}
