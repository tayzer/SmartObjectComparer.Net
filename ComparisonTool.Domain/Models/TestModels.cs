using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace ComparisonTool.Domain.Models;

/// <summary>
/// Test domain model for validating JSON and XML comparison functionality
/// Represents a customer order with various data types and nested structures
/// </summary>
[XmlRoot(ElementName = "CustomerOrder")]
public class CustomerOrder
{
    [XmlElement(ElementName = "OrderId")]
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [XmlElement(ElementName = "OrderDate")]
    [JsonPropertyName("orderDate")]
    public DateTime OrderDate { get; set; }

    [XmlElement(ElementName = "Customer")]
    [JsonPropertyName("customer")]
    public Customer Customer { get; set; } = new();

    [XmlArray(ElementName = "Items")]
    [XmlArrayItem(ElementName = "Item")]
    [JsonPropertyName("items")]
    public List<OrderItem> Items { get; set; } = new();

    [XmlElement(ElementName = "ShippingAddress")]
    [JsonPropertyName("shippingAddress")]
    public Address ShippingAddress { get; set; } = new();

    [XmlElement(ElementName = "BillingAddress")]
    [JsonPropertyName("billingAddress")]
    public Address BillingAddress { get; set; } = new();

    [XmlElement(ElementName = "Payment")]
    [JsonPropertyName("payment")]
    public PaymentInfo Payment { get; set; } = new();

    [XmlElement(ElementName = "Status")]
    [JsonPropertyName("status")]
    public OrderStatus Status { get; set; }

    [XmlElement(ElementName = "TotalAmount")]
    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; set; }

    [XmlElement(ElementName = "Notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [XmlArray(ElementName = "Tags")]
    [XmlArrayItem(ElementName = "Tag")]
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public class Customer
{
    [XmlElement(ElementName = "Id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [XmlElement(ElementName = "FirstName")]
    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [XmlElement(ElementName = "LastName")]
    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;

    [XmlElement(ElementName = "Email")]
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [XmlElement(ElementName = "Phone")]
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [XmlElement(ElementName = "IsVip")]
    [JsonPropertyName("isVip")]
    public bool IsVip { get; set; }

    [XmlElement(ElementName = "LoyaltyPoints")]
    [JsonPropertyName("loyaltyPoints")]
    public int LoyaltyPoints { get; set; }
}

public class OrderItem
{
    [XmlElement(ElementName = "ProductId")]
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    [XmlElement(ElementName = "ProductName")]
    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = string.Empty;

    [XmlElement(ElementName = "Category")]
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [XmlElement(ElementName = "Quantity")]
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [XmlElement(ElementName = "UnitPrice")]
    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    [XmlElement(ElementName = "Discount")]
    [JsonPropertyName("discount")]
    public decimal Discount { get; set; }

    [XmlElement(ElementName = "Total")]
    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    [XmlArray(ElementName = "Attributes")]
    [XmlArrayItem(ElementName = "Attribute")]
    [JsonPropertyName("attributes")]
    public List<ProductAttribute> Attributes { get; set; } = new();
}

public class ProductAttribute
{
    [XmlElement(ElementName = "Name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement(ElementName = "Value")]
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public class Address
{
    [XmlElement(ElementName = "Street")]
    [JsonPropertyName("street")]
    public string Street { get; set; } = string.Empty;

    [XmlElement(ElementName = "City")]
    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [XmlElement(ElementName = "State")]
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [XmlElement(ElementName = "PostalCode")]
    [JsonPropertyName("postalCode")]
    public string PostalCode { get; set; } = string.Empty;

    [XmlElement(ElementName = "Country")]
    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;
}

public class PaymentInfo
{
    [XmlElement(ElementName = "Method")]
    [JsonPropertyName("method")]
    public PaymentMethod Method { get; set; }

    [XmlElement(ElementName = "CardLastFour")]
    [JsonPropertyName("cardLastFour")]
    public string? CardLastFour { get; set; }

    [XmlElement(ElementName = "TransactionId")]
    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;

    [XmlElement(ElementName = "ProcessedDate")]
    [JsonPropertyName("processedDate")]
    public DateTime ProcessedDate { get; set; }
}

public enum OrderStatus
{
    [XmlEnum("Pending")]
    Pending,
    
    [XmlEnum("Processing")]
    Processing,
    
    [XmlEnum("Shipped")]
    Shipped,
    
    [XmlEnum("Delivered")]
    Delivered,
    
    [XmlEnum("Cancelled")]
    Cancelled
}

public enum PaymentMethod
{
    [XmlEnum("CreditCard")]
    CreditCard,
    
    [XmlEnum("DebitCard")]
    DebitCard,
    
    [XmlEnum("PayPal")]
    PayPal,
    
    [XmlEnum("BankTransfer")]
    BankTransfer
} 