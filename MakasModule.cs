using Autofac;
using AssettoServer.Server.Plugin;
using Microsoft.Extensions.Hosting;

namespace SonmezMakasPlugin;

public class MakasModule : AssettoServerModule<MakasConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<MakasService>().AsSelf().As<IHostedService>().SingleInstance();
    }
}
