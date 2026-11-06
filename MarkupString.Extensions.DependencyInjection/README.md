# MarkupString.Extensions.DependencyInjection

`AddMarkupString()` for `Microsoft.Extensions.DependencyInjection` hosts such as ASP.NET Core. See
[Dependency injection](https://github.com/SharpMUSH/MarkupString/blob/main/docs/dependency-injection.md).

```csharp
builder.Services.AddMarkupString()
    .BindConfiguration()                                  // "MarkupString" in appsettings.json
    .AddDecoders(registry => registry.WithMxp().WithPueblo());

// per connection
var decoder = app.Services.GetRequiredService<IDecoderFactory>().CreateDecoder();
```
