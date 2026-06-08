using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public static class RollTargets
{
    public static int DodgeTarget(Ruleset ruleset, Player player, int opposingTackleZones, int skillBonus = 0)
    {
        var twoHeadsBonus = SkillHookResolver.PlayerHasHookedEffect(ruleset, player, GameEventKind.DodgeRoll, GameEventStage.ModifyTarget, SkillEffect.TwoHeads) ? 1 : 0;
        var titchyBonus = SkillHookResolver.PlayerHasHookedEffect(ruleset, player, GameEventKind.DodgeRoll, GameEventStage.ModifyTarget, SkillEffect.Titchy) ? 1 : 0;
        var effectiveTackleZones = SkillHookResolver.PlayerHasHookedEffect(ruleset, player, GameEventKind.DodgeRoll, GameEventStage.ModifyTarget, SkillEffect.Stunty) ? 0 : opposingTackleZones;
        return Math.Clamp(player.Stats.Agility - 1 + effectiveTackleZones - skillBonus - twoHeadsBonus - titchyBonus, 2, 6);
    }

    public static int PickupTarget(Ruleset ruleset, Player player, int opposingTackleZones, WeatherCondition weather)
    {
        var hasBigHand = SkillHookResolver.PlayerHasHookedEffect(ruleset, player, GameEventKind.PickupRoll, GameEventStage.ModifyTarget, SkillEffect.BigHand);
        var markedModifier = hasBigHand ? 0 : opposingTackleZones;
        var weatherModifier = weather == WeatherCondition.PouringRain && !hasBigHand ? 1 : 0;
        var extraArmsModifier = SkillHookResolver.PlayerHasHookedEffect(ruleset, player, GameEventKind.PickupRoll, GameEventStage.ModifyTarget, SkillEffect.ExtraArms) ? -1 : 0;
        return Math.Clamp(player.Stats.Agility - 1 + markedModifier + weatherModifier + extraArmsModifier, 2, 6);
    }

    public static int CatchTarget(Ruleset ruleset, Player player, WeatherCondition weather, int opposingTackleZones = 0, int disturbingPresence = 0)
    {
        var weatherModifier = weather == WeatherCondition.PouringRain ? 1 : 0;
        var extraArmsModifier = SkillHookResolver.PlayerHasHookedEffect(ruleset, player, GameEventKind.CatchRoll, GameEventStage.ModifyTarget, SkillEffect.ExtraArms) ? -1 : 0;
        return Math.Clamp(player.Stats.Agility + weatherModifier + opposingTackleZones + disturbingPresence + extraArmsModifier, 2, 6);
    }

    public static int InterceptionTarget(Ruleset ruleset, Player player, WeatherCondition weather, int opposingTackleZones = 0, int disturbingPresence = 0)
    {
        var weatherModifier = weather == WeatherCondition.PouringRain ? 1 : 0;
        var extraArmsModifier = SkillHookResolver.PlayerHasHookedEffect(ruleset, player, GameEventKind.InterceptionRoll, GameEventStage.ModifyTarget, SkillEffect.ExtraArms) ? -1 : 0;
        var veryLongLegsModifier = SkillHookResolver.PlayerHasHookedEffect(ruleset, player, GameEventKind.InterceptionRoll, GameEventStage.ModifyTarget, SkillEffect.VeryLongLegs) ? -2 : 0;
        return Math.Clamp(player.Stats.Agility + 2 + weatherModifier + opposingTackleZones + disturbingPresence + extraArmsModifier + veryLongLegsModifier, 2, 6);
    }

    public static int PassingTarget(Ruleset ruleset, Player player, PassRange passRange, WeatherCondition weather, int opposingTackleZones = 0, int disturbingPresence = 0)
    {
        var weatherModifier = weather is WeatherCondition.VerySunny or WeatherCondition.Blizzard ? 1 : 0;
        var skillModifier = 0;
        if (SkillHookResolver.PlayerHasHookedEffect(ruleset, player, GameEventKind.PassRoll, GameEventStage.ModifyTarget, SkillEffect.Accurate) && passRange.Name is "quick" or "short")
        {
            skillModifier--;
        }
        else if (SkillHookResolver.PlayerHasHookedEffect(ruleset, player, GameEventKind.PassRoll, GameEventStage.ModifyTarget, SkillEffect.Cannoneer) && passRange.Name is "long" or "long bomb")
        {
            skillModifier--;
        }

        return Math.Clamp(player.Stats.Passing + passRange.TargetModifier + weatherModifier + opposingTackleZones + disturbingPresence + skillModifier, 2, 6);
    }

    public static int LandingTarget(Ruleset ruleset, Player player, int opposingTackleZones)
    {
        return Math.Clamp(player.Stats.Agility + opposingTackleZones, 2, 6);
    }

    public static int GoForItTarget(WeatherCondition weather)
    {
        return weather == WeatherCondition.Blizzard ? 3 : 2;
    }
}
