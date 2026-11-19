using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Zeugnis.App;
using Zeugnis.App.Services;
using Zeugnis.Exchange;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<IAeadCipher, WebCryptoAeadCipher>();
builder.Services.AddScoped<EnvelopeCodec>();
builder.Services.AddScoped<FileService>();
builder.Services.AddScoped<SchoolSession>();
builder.Services.AddScoped<TeacherSession>();

await builder.Build().RunAsync();
