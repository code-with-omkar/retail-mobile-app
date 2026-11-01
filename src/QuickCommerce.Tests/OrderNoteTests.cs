using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using QuickCommerce.Api.Security;
using QuickCommerce.Application.DTOs;
using QuickCommerce.Application.Services;
using QuickCommerce.Application.Validators;
using QuickCommerce.Domain;
using QuickCommerce.Infrastructure;
using QuickCommerce.Infrastructure.Persistence;
using QuickCommerce.Infrastructure.Security;
using Xunit;

namespace QuickCommerce.Tests;

/// <summary>P21: a note for the store and what to do when an item is out of stock.</summary>
[Collection("SqlServer")]
public sealed class OrderNoteTests
{
    private static (CartService Cart, CheckoutService Checkout, InMemoryCommerceStore Data, Store Store, Product Tomato) Create()
    {
        var data = InMemoryCommerceStore.CreateSeeded();
        var user = data.Users.Single();
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.ExternalSubject),
                    new Claim("organization_id", data.Organizations.Single().Id.ToString())
                ], "Test"))
            }
        });
        var resolver = new CurrentUserContextResolver(currentUser, data);
        var store = data.Stores.Single(item => item.Name == "Harbor Point Dark Store");
        var tomato = data.Products.Single(product => product.Name == "Tomato");
        return (new CartService(data, resolver, new AddCartItemRequestValidator(), new UpdateCartItemRequestValidator()), new CheckoutService(data, resolver, new CheckoutRequestValidator()), data, store, tomato);
    }

    private static CheckoutRequest Request(string? note = null, string? choice = null) => new("12 Main Street", 19.07, 72.87, Note: note, OutOfStockChoice: choice);

    private static async Task<CheckoutOperationResult> Place(string? note = null, string? choice = null, string? key = null)
    {
        var (cart, checkout, _, store, tomato) = Create();
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1));
        return await checkout.CheckoutAsync(store.Id, Request(note, choice), key);
    }

    [Fact]
    public async Task The_note_and_choice_are_kept_on_the_order_and_shown_in_the_response()
    {
        var (cart, checkout, data, store, tomato) = Create();
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1));

        var result = await checkout.CheckoutAsync(store.Id, Request("  Ring the bell twice  ", "Replace"));

        Assert.Equal(CheckoutOperationStatus.Succeeded, result.Status);
        Assert.Equal(("Ring the bell twice", "Replace"), (result.Order!.Note, result.Order.OutOfStockChoice));
        Assert.Equal(("Ring the bell twice", "Replace"), (data.Orders.Single().OrderNote, data.Orders.Single().OutOfStockChoice));
    }

    [Fact]
    public async Task With_nothing_given_there_is_no_note_and_the_store_will_call()
    {
        var result = await Place();

        Assert.Equal((null, OrderPreferences.CallMe), (result.Order!.Note, result.Order.OutOfStockChoice));
    }

    [Theory]
    [InlineData("Delete")]
    [InlineData("remove")]
    [InlineData("Remove,Replace")]
    public async Task An_unknown_choice_is_refused_and_nothing_is_ordered(string choice)
    {
        var (cart, checkout, data, store, tomato) = Create();
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1));

        var result = await checkout.CheckoutAsync(store.Id, Request(choice: choice));

        Assert.Equal(CheckoutOperationStatus.InvalidRequest, result.Status);
        Assert.Empty(data.Orders);
    }

    [Theory]
    [InlineData("<b>hi</b>")]
    [InlineData("bell \u0007")]
    public async Task A_note_with_markup_or_control_characters_is_refused(string note) =>
        Assert.Equal(CheckoutOperationStatus.InvalidRequest, (await Place(note)).Status);

    [Fact]
    public async Task A_note_can_be_200_characters_but_not_201_and_can_be_in_marathi()
    {
        Assert.Equal(CheckoutOperationStatus.Succeeded, (await Place(new string('a', 200))).Status);
        Assert.Equal(CheckoutOperationStatus.InvalidRequest, (await Place(new string('a', 201))).Status);
        Assert.Equal("कृपया दारावर ठेवा", (await Place("कृपया दारावर ठेवा")).Order!.Note);
    }

    [Fact]
    public async Task A_blank_note_is_no_note()
    {
        Assert.Null((await Place("   ")).Order!.Note);
    }

    [Fact]
    public async Task The_same_key_with_a_different_note_is_not_the_same_order()
    {
        var (cart, checkout, _, store, tomato) = Create();
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1));
        var first = await checkout.CheckoutAsync(store.Id, Request("one"), "key-12345678");

        var replay = await checkout.CheckoutAsync(store.Id, Request("one"), "key-12345678");
        var changed = await checkout.CheckoutAsync(store.Id, Request("two"), "key-12345678");

        Assert.Equal(CheckoutOperationStatus.Succeeded, first.Status);
        Assert.True(replay.Replayed);
        Assert.Equal((CheckoutOperationStatus.Conflict, CheckoutReasons.IdempotencyKeyReused), (changed.Status, changed.Reason));
    }

    [Fact]
    public async Task The_store_list_carries_the_note_of_each_order()
    {
        var (cart, checkout, data, store, tomato) = Create();
        await cart.AddItemAsync(store.Id, new AddCartItemRequest(tomato.Id, 1));
        await checkout.CheckoutAsync(store.Id, Request("Gate code 42"));
        data.Orders.Add(new Order { OrderNumber = "X-2", UserId = data.Users.Single().Id, StoreId = store.Id, DeliveryAddress = "x", TotalAmount = 10 });

        var list = await data.GetScopedOrderSummariesAsync(data.Organizations.Single().Id, new HashSet<Guid>(), true);

        Assert.Equal([true, false], list.OrderBy(order => order.OrderNumber == "X-2").Select(order => order.Note is not null));
    }

    [Fact]
    public async Task On_SQL_the_note_is_stored_shown_and_erased_when_the_account_is_deleted()
    {
        if (CheckoutEfIntegrationTests.ConnectionString is null)
        {
            return;
        }

        await using var world = await CheckoutEfIntegrationTests.World.CreateAsync(1, 5);
        var customer = world.CustomerIds[0];

        var placed = await world.Checkout(customer, CheckoutEfIntegrationTests.Commit(null) with { Note = "Leave at the gate", OutOfStockChoice = "Remove" });

        Assert.Equal(CheckoutCommitStatus.Succeeded, placed.Status);
        Assert.Equal(("Leave at the gate", "Remove"), (placed.Order!.Note, placed.Order.OutOfStockChoice));
        await using var db = world.Db();
        var row = await db.Orders.AsNoTracking().SingleAsync(order => order.StoreId == world.Store.Id);
        Assert.Equal(("Leave at the gate", "Remove"), (row.OrderNote, row.OutOfStockChoice));
        var summary = await new EfCommerceStore(db).GetScopedOrderSummariesAsync(await db.Stores.Where(store => store.Id == world.Store.Id).Select(store => store.OrganizationId).SingleAsync(), new HashSet<Guid> { world.Store.Id }, false);
        Assert.Equal("Leave at the gate", summary.Single(order => order.Id == row.Id).Note);
    }
}
