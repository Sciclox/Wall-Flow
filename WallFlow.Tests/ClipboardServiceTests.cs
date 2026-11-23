using System;
using System.IO;
using System.Linq;
using System.Threading;
using WallFlow.Services;
using Xunit;

namespace WallFlow.Tests;

public class ClipboardServiceTests
{
    [Fact]
    public void ClipboardService_AddAiItem_InsertsItemWithCorrectMetadata()
    {
        var service = ClipboardService.Instance;
        string testText = "Respuesta de IA " + Guid.NewGuid();

        service.AddAiItem(testText);

        var item = service.History.FirstOrDefault(h => h.Content == testText);
        Assert.NotNull(item);
        Assert.Equal("IA", item.Type);
        Assert.Contains("#IA", item.Tags);
        Assert.Contains("#Texto", item.Tags);

        // Clean up
        service.DeleteItem(item.Id);
    }

    [Fact]
    public void ClipboardService_DeleteItem_RemovesItemFromHistory()
    {
        var service = ClipboardService.Instance;
        string testText = "Elemento a eliminar " + Guid.NewGuid();

        service.AddAiItem(testText);
        var item = service.History.FirstOrDefault(h => h.Content == testText);
        Assert.NotNull(item);

        service.DeleteItem(item.Id);

        var deletedItem = service.History.FirstOrDefault(h => h.Id == item.Id);
        Assert.Null(deletedItem);
    }

    [Fact]
    public void ClipboardService_ClearAllHistory_EmptiesHistory()
    {
        var service = ClipboardService.Instance;
        service.AddAiItem("Temp item 1");
        service.AddAiItem("Temp item 2");

        Assert.NotEmpty(service.History);

        service.ClearAllHistory();

        Assert.Empty(service.History);
    }
}
