using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace PlayerModelLib;

public class CustomPlayerShapeRenderer : EntityPlayerShapeRenderer
{
    public CustomPlayerShapeRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
    {
    }

    public override void TesselateShape()
    {
        if (PlayerModelModSystem.Settings.TesselatePlayerShapeOffThread)
        {
            TyronThreadPool.QueueTask(TesselateShapeOffThread, "CustomPlayerShapeRenderer");
        }
        else
        {
            base.TesselateShape();
        }
    }

    public virtual void TesselateShapeOffThread()
    {
        try
        {
            base.TesselateShape();
        }
        catch (Exception exception)
        {
            if (PlayerModelModSystem.Settings.LogOffThreadTesselationErrors)
            {
                string message = $"Error while tesselating player shape off-thread (please report in the lib thread on discord):\n{exception}";
                LoggerUtil.Warn(entity.Api, this, message);
            }
            entity.MarkShapeModified();
        }
    }
}
