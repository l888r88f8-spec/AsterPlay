using AsterPlay.Models;
using AsterPlay.Services;

namespace AsterPlay.ViewModels;

public sealed class DetailsViewModel
{
    public DetailsViewModel(EmbyClient client, EmbyItem item)
    {
        Item = item;
        Title = item.Name;
        OriginalTitle = item.OriginalTitle;
        Overview = item.Overview;
        Tagline = item.Taglines.FirstOrDefault() ?? "";
        PosterUrl = client.BuildPrimaryUrl(item, 600);
        BackdropUrl = client.BuildBackdropUrl(item, 1920);
        Genres = item.Genres;
        Tags = item.Tags;
        Studios = item.Studios.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        Cast = item.People
            .Where(x => string.Equals(x.Type, "Actor", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Directors = item.People
            .Where(x => string.Equals(x.Type, "Director", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var streams = item.MediaSources
            .SelectMany(source => source.MediaStreams)
            .Concat(item.MediaStreams)
            .GroupBy(stream => (stream.Type, stream.Index, stream.Codec, stream.Language, stream.DisplayTitle))
            .Select(group => group.First())
            .ToArray();

        MediaStreams = streams
            .Select(stream => new MediaStreamViewModel(stream))
            .ToArray();

        MediaSources = item.MediaSources
            .Select(source => new MediaSourceViewModel(source))
            .ToArray();
    }

    public EmbyItem Item { get; }
    public string Title { get; }
    public string OriginalTitle { get; }
    public string Overview { get; }
    public string Tagline { get; }
    public string PosterUrl { get; }
    public string BackdropUrl { get; }
    public IReadOnlyList<string> Genres { get; }
    public IReadOnlyList<string> Tags { get; }
    public IReadOnlyList<string> Studios { get; }
    public IReadOnlyList<EmbyPerson> Cast { get; }
    public IReadOnlyList<EmbyPerson> Directors { get; }
    public IReadOnlyList<MediaSourceViewModel> MediaSources { get; }
    public IReadOnlyList<MediaStreamViewModel> MediaStreams { get; }

    public bool IsMovie => string.Equals(Item.Type, "Movie", StringComparison.OrdinalIgnoreCase);
    public bool IsSeries => string.Equals(Item.Type, "Series", StringComparison.OrdinalIgnoreCase);
    public bool IsEpisode => string.Equals(Item.Type, "Episode", StringComparison.OrdinalIgnoreCase);
    public bool IsFavorite => Item.UserData?.IsFavorite == true;
    public bool HasResumePosition => Item.UserData?.PlaybackPositionTicks > 0;
}

public sealed class SeasonViewModel
{
    public SeasonViewModel(EmbyClient client, EmbyItem item)
    {
        Item = item;
        Title = string.IsNullOrWhiteSpace(item.Name)
            ? item.IndexNumber is >= 0 ? $"Season {item.IndexNumber}" : "Season"
            : item.Name;
        PosterUrl = client.BuildPrimaryUrl(item, 400);
    }

    public EmbyItem Item { get; }
    public string Title { get; }
    public string PosterUrl { get; }
    public int? Number => Item.IndexNumber;
}

public sealed class EpisodeViewModel
{
    public EpisodeViewModel(EmbyClient client, EmbyItem item)
    {
        Item = item;
        Title = item.Name;
        ImageUrl = client.BuildBackdropUrl(item, 700);
    }

    public EmbyItem Item { get; }
    public string Title { get; }
    public string ImageUrl { get; }
    public string Overview => Item.Overview;
    public int? SeasonNumber => Item.ParentIndexNumber;
    public int? EpisodeNumber => Item.IndexNumber;
    public long ResumePositionTicks => Item.UserData?.PlaybackPositionTicks ?? 0;
    public double PlayedPercentage => Math.Clamp(Item.UserData?.PlayedPercentage ?? 0, 0, 100);

    public string EpisodeLabel =>
        SeasonNumber is >= 0 && EpisodeNumber is >= 0
            ? $"S{SeasonNumber:00}E{EpisodeNumber:00}"
            : EpisodeNumber is >= 0
                ? $"E{EpisodeNumber:00}"
                : "";
}

public sealed class MediaSourceViewModel
{
    public MediaSourceViewModel(MediaSource source)
    {
        Source = source;
        Streams = source.MediaStreams
            .Select(stream => new MediaStreamViewModel(stream))
            .ToArray();
    }

    public MediaSource Source { get; }
    public IReadOnlyList<MediaStreamViewModel> Streams { get; }
    public string Name => string.IsNullOrWhiteSpace(Source.Name) ? Source.Container : Source.Name;
    public string Container => Source.Container;
    public long? Size => Source.Size;
    public int? Bitrate => Source.Bitrate;
}

public sealed class MediaStreamViewModel
{
    public MediaStreamViewModel(EmbyMediaStream stream)
    {
        Stream = stream;
    }

    public EmbyMediaStream Stream { get; }
    public string Type => Stream.Type;
    public string Codec => Stream.Codec;
    public string Language => Stream.Language;
    public string Title => Stream.DisplayTitle.Length > 0 ? Stream.DisplayTitle : Stream.Title;
    public int Index => Stream.Index;
    public int? Width => Stream.Width;
    public int? Height => Stream.Height;
    public int? Channels => Stream.Channels;
    public int? BitRate => Stream.BitRate;
    public bool IsDefault => Stream.IsDefault;
    public bool IsForced => Stream.IsForced;
    public bool IsExternal => Stream.IsExternal;
}
