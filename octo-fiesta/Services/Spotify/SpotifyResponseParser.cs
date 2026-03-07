using System.Text.Json;

namespace octo_fiesta.Services.Spotify;

/// <summary>
/// Parses Spotify GraphQL responses into simple structures.
/// Ported from SpotiFLAC spotfetch.go FilterSearch and FilterPlaylist.
/// </summary>
internal static class SpotifyResponseParser
{
    public static List<SpotifyPlaylistSearchResult> ParseSearchPlaylists(JsonElement root)
    {
        var list = new List<SpotifyPlaylistSearchResult>();
        var playlistsData = GetMap(GetMap(root, "data"), "searchV2");
        if (playlistsData.ValueKind == JsonValueKind.Undefined) return list;

        var items = GetItems(playlistsData, "playlistsV2", "playlists");
        foreach (var item in items)
        {
            var playlist = GetMap(item, "data");
            if (playlist.ValueKind == JsonValueKind.Undefined)
                playlist = GetMap(item, "playlist");
            if (playlist.ValueKind == JsonValueKind.Undefined) continue;

            var id = ExtractIdFromUri(GetStr(playlist, "uri"));
            var name = GetStr(playlist, "name");
            if (string.IsNullOrEmpty(name)) continue;

            var cover = ExtractCoverUrl(playlist);
            var ownerData = GetMap(GetMap(playlist, "ownerV2"), "data");
            var ownerName = GetStr(ownerData, "name");

            list.Add(new SpotifyPlaylistSearchResult(id, name, cover, ownerName));
        }
        return list;
    }

    public static SpotifyPlaylistDetail? ParsePlaylist(JsonElement root, string playlistId)
    {
        var playlistData = GetMap(GetMap(root, "data"), "playlistV2");
        if (playlistData.ValueKind == JsonValueKind.Undefined) return null;

        var name = GetStr(playlistData, "name");
        var description = GetStr(playlistData, "description");
        var id = ExtractIdFromUri(GetStr(playlistData, "uri"));
        if (string.IsNullOrEmpty(id)) id = playlistId;

        var ownerData = GetMap(GetMap(playlistData, "ownerV2"), "data");
        var ownerName = GetStr(ownerData, "name");
        var cover = ExtractPlaylistCoverUrl(playlistData);

        var content = GetMap(playlistData, "content");
        var totalCount = (int)GetNum(content, "totalCount");
        var tracks = ParsePlaylistTracks(content);

        var followers = 0.0;
        if (playlistData.TryGetProperty("followers", out var fol))
        {
            if (fol.ValueKind == JsonValueKind.Object && fol.TryGetProperty("totalCount", out var tc))
                followers = tc.GetDouble();
            else if (fol.ValueKind == JsonValueKind.Number)
                followers = fol.GetDouble();
        }

        return new SpotifyPlaylistDetail(id, name, description, ownerName, cover, totalCount, (int)followers, tracks);
    }

    public static List<SpotifyPlaylistTrack> ParsePlaylistTracks(JsonElement content)
    {
        var list = new List<SpotifyPlaylistTrack>();
        var items = GetArray(content, "items");
        foreach (var item in items)
        {
            var trackData = GetMap(GetMap(item, "itemV2"), "data");
            if (trackData.ValueKind == JsonValueKind.Undefined) continue;

            var id = GetStr(trackData, "id");
            if (string.IsNullOrEmpty(id))
                id = ExtractIdFromUri(GetStr(trackData, "uri"));
            if (string.IsNullOrEmpty(id)) continue;

            var title = GetStr(trackData, "name");
            if (string.IsNullOrEmpty(title)) continue;

            var artists = GetMap(trackData, "artists");
            var artistNames = new List<string>();
            foreach (var a in GetArray(artists, "items"))
            {
                var profile = GetMap(a, "profile");
                var n = GetStr(profile, "name");
                if (!string.IsNullOrEmpty(n)) artistNames.Add(n);
            }
            var artistStr = string.Join(", ", artistNames);

            var durationMs = 0.0;
            if (trackData.TryGetProperty("trackDuration", out var td))
                durationMs = GetNum(td, "totalMilliseconds");
            else if (trackData.TryGetProperty("duration", out var d))
                durationMs = GetNum(d, "totalMilliseconds");

            var albumData = GetMap(trackData, "albumOfTrack");
            var albumName = GetStr(albumData, "name");
            var albumId = ExtractIdFromUri(GetStr(albumData, "uri"));

            list.Add(new SpotifyPlaylistTrack(id, title, artistStr, albumName, albumId, (int)(durationMs / 1000)));
        }
        return list;
    }

    private static string ExtractIdFromUri(string uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.Contains(':')) return "";
        var parts = uri.Split(':');
        return parts[^1];
    }

    private static string ExtractCoverUrl(JsonElement playlist)
    {
        var images = GetMap(playlist, "images");
        if (images.ValueKind == JsonValueKind.Undefined)
            images = GetMap(playlist, "imagesV2");
        if (images.ValueKind == JsonValueKind.Undefined) return "";

        var items = GetArray(images, "items");
        if (items.Length > 0 && items[0].TryGetProperty("sources", out var srcs))
        {
            foreach (var s in srcs.EnumerateArray())
            {
                if (s.TryGetProperty("url", out var u))
                    return u.GetString() ?? "";
            }
        }
        var sources = GetArray(images, "sources");
        if (sources.Length > 0 && sources[0].TryGetProperty("url", out var url))
            return url.GetString() ?? "";
        return "";
    }

    private static string ExtractPlaylistCoverUrl(JsonElement playlistData)
    {
        var images = GetMap(playlistData, "images");
        if (images.ValueKind == JsonValueKind.Undefined)
            images = GetMap(playlistData, "imagesV2");
        if (images.ValueKind == JsonValueKind.Undefined) return "";

        var items = GetArray(images, "items");
        if (items.Length > 0 && items[0].TryGetProperty("sources", out var srcs))
        {
            foreach (var s in srcs.EnumerateArray())
            {
                if (s.TryGetProperty("url", out var u))
                    return u.GetString() ?? "";
            }
        }
        var sources = GetArray(images, "sources");
        if (sources.Length > 0 && sources[0].TryGetProperty("url", out var url))
            return url.GetString() ?? "";
        return "";
    }

    private static JsonElement GetMap(JsonElement el, string key)
    {
        if (el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return default;
        if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object)
            return v;
        return default;
    }

    private static string GetStr(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return "";
        if (el.TryGetProperty(key, out var v))
            return v.GetString() ?? "";
        return "";
    }

    private static double GetNum(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Undefined) return 0;
        if (el.TryGetProperty(key, out var v) && v.TryGetDouble(out var d))
            return d;
        return 0;
    }

    private static JsonElement[] GetArray(JsonElement el, string key)
    {
        if (el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return Array.Empty<JsonElement>();
        if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array)
            return v.EnumerateArray().ToArray();
        return Array.Empty<JsonElement>();
    }

    private static IEnumerable<JsonElement> GetItems(JsonElement parent, string key1, string key2)
    {
        var data = GetMap(parent, key1);
        if (data.ValueKind == JsonValueKind.Undefined)
            data = GetMap(parent, key2);
        var items = GetArray(data, "items");
        return items;
    }
}

internal record SpotifyPlaylistSearchResult(string Id, string Name, string CoverUrl, string Owner);

internal record SpotifyPlaylistDetail(string Id, string Name, string Description, string OwnerName, string CoverUrl, int TrackCount, int Followers, List<SpotifyPlaylistTrack> Tracks);

internal record SpotifyPlaylistTrack(string SpotifyId, string Title, string Artist, string AlbumName, string AlbumId, int DurationSeconds);
