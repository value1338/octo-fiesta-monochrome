using Microsoft.AspNetCore.Mvc;
using octo_fiesta.Models.Domain;
using octo_fiesta.Services.Subsonic;
using System.Xml.Linq;

namespace octo_fiesta.Tests;

public class SubsonicResponseBuilderExtraTests
{
    private readonly SubsonicResponseBuilder _builder;

    public SubsonicResponseBuilderExtraTests()
    {
        _builder = new SubsonicResponseBuilder();
    }

    [Fact]
    public void CreateSongResponse_Xml_EstimatesSizeFromBitRate()
    {
        // Arrange
        var song = new Song
        {
            Id = "song-ext-1",
            Title = "Ext Song",
            Artist = "Artist",
            Album = "Album",
            AlbumId = "album1",
            Duration = 100, // seconds
            ExternalProvider = "SquidWTF",
            IsLocal = false
        };

        // Act
        var result = _builder.CreateSongResponse("xml", song);

        // Assert
        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/xml; charset=utf-8", contentResult.ContentType);

        var doc = XDocument.Parse(contentResult.Content!);
        var ns = doc.Root!.GetDefaultNamespace();
        var songElement = doc.Root!.Element(ns + "song");
        Assert.NotNull(songElement);

        // bitRate for SquidWTF is 1141 (kbps)
        // estimated size = 1141 kbps * 125 bytes/sec per kbps * 100 sec
        long expected = 1141L * 125L * 100L;
        Assert.Equal(expected.ToString(), songElement.Attribute("size")?.Value);
    }

    [Fact]
    public void CreateAlbumResponse_Xml_OmitsEmptyGenre()
    {
        // Arrange
        var album = new Album
        {
            Id = "album-genre-1",
            Title = "No Genre Album",
            Artist = "Artist",
            Songs = new List<Song>
            {
                new Song { Id = "s1", Title = "S1" }
            }
        };

        // Act
        var result = _builder.CreateAlbumResponse("xml", album);

        // Assert
        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/xml; charset=utf-8", contentResult.ContentType);

        var doc = XDocument.Parse(contentResult.Content!);
        var ns = doc.Root!.GetDefaultNamespace();
        var albumElement = doc.Root!.Element(ns + "album");
        Assert.NotNull(albumElement);
        Assert.Null(albumElement.Attribute("genre"));
    }
}