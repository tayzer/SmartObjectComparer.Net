// <copyright file="TestModels.cs" company="PlaceholderCompany">
using System.Text.Json.Serialization;

namespace ComparisonTool.Domain.Models;

/// <summary>
/// Test domain model for validating JSON comparison functionality
/// Represents a customer order with various data types and nested structures.
/// </summary>
public class CustomerOrder
{
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("orderDate")]
    public DateTime OrderDate
    {
        get; set;
    }

    [JsonPropertyName("customer")]
    public Customer Customer { get; set; } = new Customer();

    [JsonPropertyName("items")]
    public List<OrderItem> Items { get; set; } = new List<OrderItem>();

    [JsonPropertyName("shippingAddress")]
    public Address ShippingAddress { get; set; } = new Address();

    [JsonPropertyName("billingAddress")]
    public Address BillingAddress { get; set; } = new Address();

    [JsonPropertyName("payment")]
    public PaymentInfo Payment { get; set; } = new PaymentInfo();

    [JsonPropertyName("status")]
    public OrderStatus Status
    {
        get; set;
    }

    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount
    {
        get; set;
    }

    [JsonPropertyName("notes")]
    public string? Notes
    {
        get; set;
    }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new List<string>();
}

public class Customer
{
    [JsonPropertyName("id")]
    public int Id
    {
        get; set;
    }

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string? Phone
    {
        get; set;
    }

    [JsonPropertyName("isVip")]
    public bool IsVip
    {
        get; set;
    }

    [JsonPropertyName("loyaltyPoints")]
    public int LoyaltyPoints
    {
        get; set;
    }
}

public class OrderItem
{
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity
    {
        get; set;
    }

    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice
    {
        get; set;
    }

    [JsonPropertyName("discount")]
    public decimal Discount
    {
        get; set;
    }

    [JsonPropertyName("total")]
    public decimal Total
    {
        get; set;
    }

    [JsonPropertyName("attributes")]
    public List<ProductAttribute> Attributes { get; set; } = new List<ProductAttribute>();
}

public class ProductAttribute
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class Address
{
    [JsonPropertyName("street")]
    public string Street { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("postalCode")]
    public string PostalCode { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;
}

public class PaymentInfo
{
    [JsonPropertyName("method")]
    public PaymentMethod Method
    {
        get; set;
    }

    [JsonPropertyName("cardLastFour")]
    public string? CardLastFour
    {
        get; set;
    }

    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonPropertyName("processedDate")]
    public DateTime ProcessedDate
    {
        get; set;
    }
}

public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered,
    Cancelled,
}

public enum PaymentMethod
{
    CreditCard,
    DebitCard,
    PayPal,
    BankTransfer,
}
