using SoloBB.Core.Domain;
using SoloBB.Core.Services;

var smoke = new SmokeRun();
var fixture = await SmokeFixture.LoadAsync();
var root = fixture.Root;
var store = fixture.Store;
var ruleset = fixture.Ruleset;
var rosterSet = fixture.RosterSet;

smoke.StartSection("Ruleset catalog and data loading");
Assert(ruleset.Id == "bb2020-lite", "ruleset id should load");
Assert(rosterSet.Rosters.Count >= 20, "sample roster set should contain the expanded BB2020 team catalog");
Assert(rosterSet.StarPlayers.Count >= 6, "sample roster set should contain the expanded star player sample data");
Assert(ruleset.Inducements.Count >= 8, "ruleset should contain the expanded inducement sample catalog");
Assert(ruleset.Inducements.Any(inducement => inducement.Id == "bribe"), "ruleset should contain inducement data");
Assert(rosterSet.Rosters.Any(roster => roster.RosterRestrictions.Contains("mixed-position-animosity")), "sample roster data should include roster restriction metadata");
Assert(rosterSet.Rosters.Single(roster => roster.Id == "human").Positions.Any(position => position.Id == "ogre" && position.Max == 1), "Human roster should include one Ogre");
Assert(rosterSet.Rosters.Single(roster => roster.Id == "dwarf").Positions.Any(position => position.Id == "deathroller" && position.Max == 1), "Dwarf roster should include one Deathroller");
var untrainedTroll = rosterSet.Rosters.Single(roster => roster.Id == "orc").Positions.Single(position => position.Id == "troll");
Assert(untrainedTroll.Name == "Untrained Troll" && untrainedTroll.Max == 1 && untrainedTroll.Stats.Strength == 5, "Orc roster should include one strength 5 Untrained Troll");
Assert(untrainedTroll.StartingSkills.Contains("projectile-vomit") && untrainedTroll.StartingSkills.Contains("really-stupid"), "Untrained Troll should load its defining traits");
Assert(ruleset.Skills.Single(skill => skill.Id == "animosity").DataOnly, "unimplemented roster traits should be explicitly marked data-only");
AssertThrowsInvalidData(
    () => new RulesetValidator().Validate(ruleset with
    {
        Skills =
        [
            .. ruleset.Skills,
            new SkillDefinition
            {
                Id = "unknown-coverage",
                Name = "Unknown Coverage",
                Category = "trait"
            }
        ]
    }),
    "ruleset validation should reject skills with no behavior coverage unless marked data-only");
AssertThrowsInvalidData(
    () => new RulesetValidator().Validate(ruleset with
    {
        Skills =
        [
            .. ruleset.Skills.Select(skill => skill.Id == "block" ? skill with { Category = "mystery" } : skill)
        ]
    }),
    "ruleset validation should reject unknown skill categories");
AssertThrowsInvalidData(
    () => new RulesetValidator().Validate(ruleset with
    {
        Inducements =
        [
            .. ruleset.Inducements,
            new InducementDefinition
            {
                Id = "mystery-inducement",
                Name = "Mystery Inducement",
                Kind = "mystery",
                Description = "Invalid metadata for validation."
            }
        ]
    }),
    "ruleset validation should reject unknown inducement kinds");
AssertThrowsInvalidData(
    () => new RulesetValidator().Validate(ruleset with
    {
        AdvancementThresholds = ruleset.AdvancementThresholds
            .Where(pair => pair.Key != "randomPrimary")
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
    }),
    "ruleset validation should require all known advancement thresholds");
AssertThrowsInvalidData(
    () => new RulesetValidator().Validate(ruleset with { Dice = null! }),
    "ruleset validation should reject missing dice rules with a data error");
Assert(ruleset.Skills.Single(skill => skill.Id == "block").Effects.Contains(SkillEffect.BothDownProtection), "block skill should declare its ruleset mechanic");
Assert(HasHook(ruleset, "block", GameEventKind.BlockRoll, GameEventStage.BeforeResolve), "block skill should declare its block resolution hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "dauntless").Effects.Contains(SkillEffect.Dauntless), "dauntless should declare its strength challenge mechanic");
Assert(HasHook(ruleset, "dauntless", GameEventKind.BlockRoll, GameEventStage.BeforeRoll), "dauntless should declare its block roll hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "dodge").Effects.Contains(SkillEffect.DodgeReroll), "dodge skill should declare its reroll mechanic");
Assert(HasHook(ruleset, "dodge", GameEventKind.DodgeRoll, GameEventStage.AfterRoll), "dodge skill should declare its dodge roll hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "tackle").Effects.Contains(SkillEffect.CancelDodgeReroll), "tackle skill should declare its dodge-cancel mechanic");
Assert(HasHook(ruleset, "tackle", GameEventKind.DodgeRoll, GameEventStage.AfterRoll), "tackle skill should declare its opposing dodge roll hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "guard").Effects.Contains(SkillEffect.GuardAssist), "guard skill should declare its assist mechanic");
Assert(HasHook(ruleset, "guard", GameEventKind.BlockRoll, GameEventStage.ModifyTarget), "guard should declare its block assist hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "frenzy").Effects.Contains(SkillEffect.Frenzy), "frenzy should declare its forced-block mechanic");
Assert(HasHook(ruleset, "frenzy", GameEventKind.Push, GameEventStage.AfterEvent), "frenzy should declare its post-push hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "fend").Effects.Contains(SkillEffect.Fend), "fend should declare its follow-up prevention mechanic");
Assert(HasHook(ruleset, "fend", GameEventKind.Push, GameEventStage.AfterEvent), "fend should declare its post-push hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "wrestle").Effects.Contains(SkillEffect.Wrestle), "wrestle should declare its both-down mechanic");
Assert(HasHook(ruleset, "wrestle", GameEventKind.BlockRoll, GameEventStage.BeforeResolve), "wrestle should declare its block resolution hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "kick").Effects.Contains(SkillEffect.Kick), "kick should declare its kickoff scatter mechanic");
Assert(HasHook(ruleset, "kick", GameEventKind.Kickoff, GameEventStage.BeforeEvent), "kick should declare its kickoff hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "pro").Effects.Contains(SkillEffect.Pro), "pro should declare its conditional reroll mechanic");
Assert(HasHook(ruleset, "pro", GameEventKind.DodgeRoll, GameEventStage.AfterRoll), "pro should declare its dodge reroll hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "shadowing").Effects.Contains(SkillEffect.Shadowing), "shadowing should declare its follow mechanic");
Assert(HasHook(ruleset, "shadowing", GameEventKind.MoveStep, GameEventStage.AfterEvent), "shadowing should declare its movement hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "strip-ball").Effects.Contains(SkillEffect.StripBall), "strip ball should declare its ball-loosening mechanic");
Assert(HasHook(ruleset, "strip-ball", GameEventKind.Push, GameEventStage.BeforeResolve), "strip ball should declare its push resolution hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "mighty-blow").Effects.Contains(SkillEffect.MightyBlow), "mighty blow should declare its injury-pressure mechanic");
Assert(HasHook(ruleset, "mighty-blow", GameEventKind.ArmorRoll, GameEventStage.AfterRoll), "mighty blow should declare its armor roll hook");
Assert(HasHook(ruleset, "mighty-blow", GameEventKind.InjuryRoll, GameEventStage.AfterRoll), "mighty blow should declare its injury roll hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "stand-firm").Effects.Contains(SkillEffect.StandFirm), "stand firm should declare its push-prevention mechanic");
Assert(HasHook(ruleset, "stand-firm", GameEventKind.Push, GameEventStage.BeforeResolve), "stand firm should declare its push resolution hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "dirty-player").Effects.Contains(SkillEffect.DirtyPlayer), "dirty player should declare its foul-pressure mechanic");
Assert(HasHook(ruleset, "dirty-player", GameEventKind.ArmorRoll, GameEventStage.AfterRoll), "dirty player should declare its armor roll hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "thick-skull").Effects.Contains(SkillEffect.ThickSkull), "thick skull should declare its knockout-resistance mechanic");
Assert(HasHook(ruleset, "thick-skull", GameEventKind.InjuryRoll, GameEventStage.BeforeResolve), "thick skull should declare its injury resolution hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "diving-catch").Effects.Contains(SkillEffect.DivingCatch), "diving catch should declare its catch-zone mechanic");
Assert(HasHook(ruleset, "diving-catch", GameEventKind.CatchRoll, GameEventStage.BeforeEvent), "diving catch should declare its catch roll hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "diving-tackle").Effects.Contains(SkillEffect.DivingTackle), "diving tackle should declare its dodge-pressure mechanic");
Assert(ruleset.Skills.Single(skill => skill.Id == "defensive").Effects.Contains(SkillEffect.Defensive), "defensive should declare its guard-cancel mechanic");
Assert(ruleset.Skills.Single(skill => skill.Id == "jump-up").Effects.Contains(SkillEffect.JumpUp), "jump up should declare its stand-up movement mechanic");
Assert(HasHook(ruleset, "jump-up", GameEventKind.MoveStep, GameEventStage.BeforeEvent), "jump up should declare its movement hook");
Assert(HasHook(ruleset, "jump-up", GameEventKind.BlockRoll, GameEventStage.BeforeEvent), "jump up should declare its block hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "leap").Effects.Contains(SkillEffect.Leap), "leap should declare its special movement mechanic");
Assert(HasHook(ruleset, "leap", GameEventKind.MoveStep, GameEventStage.BeforeEvent), "leap should declare its movement hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "safe-pair-of-hands").Effects.Contains(SkillEffect.SafePairOfHands), "safe pair of hands should declare its ball-placement mechanic");
Assert(HasHook(ruleset, "safe-pair-of-hands", GameEventKind.BallScatter, GameEventStage.BeforeEvent), "safe pair of hands should declare its ball scatter hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "sidestep").Effects.Contains(SkillEffect.SideStep), "sidestep should declare its push-choice mechanic");
Assert(ruleset.Skills.Single(skill => skill.Id == "sneaky-git").Effects.Contains(SkillEffect.SneakyGit), "sneaky git should declare its foul-sendoff mechanic");
Assert(HasHook(ruleset, "sneaky-git", GameEventKind.ArmorRoll, GameEventStage.AfterRoll), "sneaky git should declare its armor roll hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "sprint").Effects.Contains(SkillEffect.Sprint), "sprint should declare its extra go-for-it mechanic");
Assert(HasHook(ruleset, "sprint", GameEventKind.MoveStep, GameEventStage.BeforeEvent), "sprint should declare its movement hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "arm-bar").Effects.Contains(SkillEffect.ArmBar), "arm bar should declare its failed-movement injury mechanic");
Assert(ruleset.Skills.Single(skill => skill.Id == "brawler").Effects.Contains(SkillEffect.Brawler), "brawler should declare its both-down reroll mechanic");
Assert(HasHook(ruleset, "brawler", GameEventKind.BlockRoll, GameEventStage.AfterRoll), "brawler should declare its block after-roll hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "break-tackle").Effects.Contains(SkillEffect.BreakTackle), "break tackle should declare its dodge modifier mechanic");
Assert(HasHook(ruleset, "break-tackle", GameEventKind.DodgeRoll, GameEventStage.ModifyTarget), "break tackle should declare its dodge target hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "grab").Effects.Contains(SkillEffect.Grab), "grab should declare its push-control mechanic");
Assert(HasHook(ruleset, "grab", GameEventKind.Push, GameEventStage.BeforeResolve), "grab should declare its push resolution hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "juggernaut").Effects.Contains(SkillEffect.Juggernaut), "juggernaut should declare its blitz block mechanic");
Assert(HasHook(ruleset, "juggernaut", GameEventKind.BlockRoll, GameEventStage.BeforeResolve), "juggernaut should declare its block resolution hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "multiple-block").Effects.Contains(SkillEffect.MultipleBlock), "multiple block should declare its multi-block mechanic");
Assert(HasHook(ruleset, "multiple-block", GameEventKind.BlockRoll, GameEventStage.BeforeEvent), "multiple block should declare its block hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "pile-driver").Effects.Contains(SkillEffect.PileDriver), "pile driver should declare its follow-up foul mechanic");
Assert(HasHook(ruleset, "pile-driver", GameEventKind.Push, GameEventStage.AfterEvent), "pile driver should declare its post-push hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "strong-arm").Effects.Contains(SkillEffect.StrongArm), "strong arm should declare its throw-team-mate mechanic");
Assert(HasHook(ruleset, "strong-arm", GameEventKind.PassRoll, GameEventStage.ModifyTarget), "strong arm should declare its pass target hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "accurate").Effects.Contains(SkillEffect.Accurate), "accurate should declare its short-pass mechanic");
Assert(HasHook(ruleset, "accurate", GameEventKind.PassRoll, GameEventStage.ModifyTarget), "accurate should declare its pass target hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "cannoneer").Effects.Contains(SkillEffect.Cannoneer), "cannoneer should declare its long-pass mechanic");
Assert(ruleset.Skills.Single(skill => skill.Id == "cloud-burster").Effects.Contains(SkillEffect.CloudBurster), "cloud burster should declare its interference mechanic");
Assert(HasHook(ruleset, "cloud-burster", GameEventKind.InterceptionRoll, GameEventStage.AfterRoll), "cloud burster should declare its interception hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "dump-off").Effects.Contains(SkillEffect.DumpOff), "dump-off should declare its block-interrupt mechanic");
Assert(HasHook(ruleset, "dump-off", GameEventKind.PassRoll, GameEventStage.BeforeEvent), "dump-off should declare its pass hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "fumblerooskie").Effects.Contains(SkillEffect.Fumblerooskie), "fumblerooskie should declare its ball-drop mechanic");
Assert(HasHook(ruleset, "fumblerooskie", GameEventKind.MoveStep, GameEventStage.AfterEvent), "fumblerooskie should declare its movement hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "hail-mary-pass").Effects.Contains(SkillEffect.HailMaryPass), "hail mary pass should declare its special pass mechanic");
Assert(HasHook(ruleset, "hail-mary-pass", GameEventKind.PassRoll, GameEventStage.BeforeEvent), "hail mary pass should declare its pass hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "leader").Effects.Contains(SkillEffect.Leader), "leader should declare its leader-reroll mechanic");
Assert(HasHook(ruleset, "leader", GameEventKind.ActionStart, GameEventStage.BeforeEvent), "leader should declare its action hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "nerves-of-steel").Effects.Contains(SkillEffect.NervesOfSteel), "nerves of steel should declare its tackle-zone ignore mechanic");
Assert(HasHook(ruleset, "nerves-of-steel", GameEventKind.PassRoll, GameEventStage.ModifyTarget), "nerves of steel should declare its pass target hook");
Assert(HasHook(ruleset, "nerves-of-steel", GameEventKind.CatchRoll, GameEventStage.ModifyTarget), "nerves of steel should declare its catch target hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "on-the-ball").Effects.Contains(SkillEffect.OnTheBall), "on the ball should declare its reposition mechanic");
Assert(HasHook(ruleset, "on-the-ball", GameEventKind.MoveStep, GameEventStage.BeforeEvent), "on the ball should declare its movement hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "running-pass").Effects.Contains(SkillEffect.RunningPass), "running pass should declare its continue-moving mechanic");
Assert(HasHook(ruleset, "running-pass", GameEventKind.PassRoll, GameEventStage.AfterEvent), "running pass should declare its pass continuation hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "safe-pass").Effects.Contains(SkillEffect.SafePass), "safe pass should declare its fumble-prevention mechanic");
Assert(HasHook(ruleset, "safe-pass", GameEventKind.PassRoll, GameEventStage.AfterRoll), "safe pass should declare its pass roll hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "big-hand").Effects.Contains(SkillEffect.BigHand), "big hand should declare its pickup mechanic");
Assert(ruleset.Skills.Single(skill => skill.Id == "claws").Effects.Contains(SkillEffect.Claws), "claws should declare its armor mechanic");
Assert(HasHook(ruleset, "claws", GameEventKind.ArmorRoll, GameEventStage.BeforeResolve), "claws should declare its armor resolution hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "disturbing-presence").Effects.Contains(SkillEffect.DisturbingPresence), "disturbing presence should declare its pass pressure mechanic");
Assert(HasHook(ruleset, "disturbing-presence", GameEventKind.PassRoll, GameEventStage.ModifyTarget), "disturbing presence should declare its pass target hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "extra-arms").Effects.Contains(SkillEffect.ExtraArms), "extra arms should declare its ball handling mechanic");
Assert(HasHook(ruleset, "extra-arms", GameEventKind.PickupRoll, GameEventStage.ModifyTarget), "extra arms should declare its pickup target hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "foul-appearance").Effects.Contains(SkillEffect.FoulAppearance), "foul appearance should declare its targeting mechanic");
Assert(HasHook(ruleset, "foul-appearance", GameEventKind.BlockRoll, GameEventStage.BeforeEvent), "foul appearance should declare its block hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "horns").Effects.Contains(SkillEffect.Horns), "horns should declare its blitz strength mechanic");
Assert(HasHook(ruleset, "horns", GameEventKind.BlockRoll, GameEventStage.BeforeRoll), "horns should declare its block roll hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "iron-hard-skin").Effects.Contains(SkillEffect.IronHardSkin), "iron hard skin should declare its armor protection mechanic");
Assert(HasHook(ruleset, "iron-hard-skin", GameEventKind.ArmorRoll, GameEventStage.BeforeResolve), "iron hard skin should declare its armor resolution hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "monstrous-mouth").Effects.Contains(SkillEffect.MonstrousMouth), "monstrous mouth should declare its catch mechanic");
Assert(ruleset.Skills.Single(skill => skill.Id == "prehensile-tail").Effects.Contains(SkillEffect.PrehensileTail), "prehensile tail should declare its movement pressure mechanic");
Assert(ruleset.Skills.Single(skill => skill.Id == "tentacles").Effects.Contains(SkillEffect.Tentacles), "tentacles should declare its hold mechanic");
Assert(HasHook(ruleset, "tentacles", GameEventKind.MoveStep, GameEventStage.BeforeResolve), "tentacles should declare its movement hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "two-heads").Effects.Contains(SkillEffect.TwoHeads), "two heads should declare its dodge mechanic");
Assert(ruleset.Skills.Single(skill => skill.Id == "very-long-legs").Effects.Contains(SkillEffect.VeryLongLegs), "very long legs should declare its leap and interference mechanic");
Assert(HasHook(ruleset, "very-long-legs", GameEventKind.InterceptionRoll, GameEventStage.ModifyTarget), "very long legs should declare its interception target hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "always-hungry").Effects.Contains(SkillEffect.AlwaysHungry), "always hungry should declare its throw-team-mate mechanic");
Assert(HasHook(ruleset, "always-hungry", GameEventKind.ThrowTeamMate, GameEventStage.BeforeEvent), "always hungry should declare its throw-team-mate hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "ball-and-chain").Effects.Contains(SkillEffect.BallAndChain), "ball and chain should declare its random movement mechanic");
Assert(HasHook(ruleset, "ball-and-chain", GameEventKind.MoveStep, GameEventStage.BeforeEvent), "ball and chain should declare its movement hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "bombardier").Effects.Contains(SkillEffect.Bombardier), "bombardier should declare its bomb throw mechanic");
Assert(HasHook(ruleset, "bombardier", GameEventKind.BombThrow, GameEventStage.BeforeEvent), "bombardier should declare its bomb throw hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "breathe-fire").Effects.Contains(SkillEffect.BreatheFire), "breathe fire should declare its special action mechanic");
Assert(HasHook(ruleset, "breathe-fire", GameEventKind.SpecialAction, GameEventStage.BeforeEvent), "breathe fire should declare its special action hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "chainsaw").Effects.Contains(SkillEffect.Chainsaw), "chainsaw should declare its special action mechanic");
Assert(HasHook(ruleset, "chainsaw", GameEventKind.SpecialAction, GameEventStage.BeforeEvent), "chainsaw should declare its special action hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "hypnotic-gaze").Effects.Contains(SkillEffect.HypnoticGaze), "hypnotic gaze should declare its special action mechanic");
Assert(HasHook(ruleset, "hypnotic-gaze", GameEventKind.SpecialAction, GameEventStage.BeforeEvent), "hypnotic gaze should declare its special action hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "kick-team-mate").Effects.Contains(SkillEffect.KickTeamMate), "kick team-mate should declare its launch mechanic");
Assert(HasHook(ruleset, "kick-team-mate", GameEventKind.KickTeamMate, GameEventStage.BeforeEvent), "kick team-mate should declare its launch hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "loner").Effects.Contains(SkillEffect.Loner), "loner should declare its reroll restriction mechanic");
Assert(HasHook(ruleset, "loner", GameEventKind.DodgeRoll, GameEventStage.AfterRoll), "loner should declare its reroll hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "pick-me-up").Effects.Contains(SkillEffect.PickMeUp), "pick-me-up should declare its recovery mechanic");
Assert(HasHook(ruleset, "pick-me-up", GameEventKind.DriveEnd, GameEventStage.AfterEvent), "pick-me-up should declare its drive end hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "plague-ridden").Effects.Contains(SkillEffect.PlagueRidden), "plague ridden should declare its post-match mechanic");
Assert(HasHook(ruleset, "plague-ridden", GameEventKind.PostMatch, GameEventStage.AfterEvent), "plague ridden should declare its post-match hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "projectile-vomit").Effects.Contains(SkillEffect.ProjectileVomit), "projectile vomit should declare its special action mechanic");
Assert(HasHook(ruleset, "projectile-vomit", GameEventKind.SpecialAction, GameEventStage.BeforeEvent), "projectile vomit should declare its special action hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "right-stuff").Effects.Contains(SkillEffect.RightStuff), "right stuff should declare its launch eligibility mechanic");
Assert(HasHook(ruleset, "right-stuff", GameEventKind.ThrowTeamMate, GameEventStage.BeforeEvent), "right stuff should declare its throw-team-mate hook");
Assert(HasHook(ruleset, "right-stuff", GameEventKind.KickTeamMate, GameEventStage.BeforeEvent), "right stuff should declare its kick-team-mate hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "secret-weapon").Effects.Contains(SkillEffect.SecretWeapon), "secret weapon should declare its send-off mechanic");
Assert(HasHook(ruleset, "secret-weapon", GameEventKind.DriveEnd, GameEventStage.BeforeResolve), "secret weapon should declare its drive end hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "stab").Effects.Contains(SkillEffect.Stab), "stab should declare its special action mechanic");
Assert(HasHook(ruleset, "stab", GameEventKind.SpecialAction, GameEventStage.BeforeEvent), "stab should declare its special action hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "stunty").Effects.Contains(SkillEffect.Stunty), "stunty should declare its dodge and injury mechanic");
Assert(HasHook(ruleset, "stunty", GameEventKind.DodgeRoll, GameEventStage.ModifyTarget), "stunty should declare its dodge hook");
Assert(ruleset.Skills.Single(skill => skill.Id == "swoop").Effects.Contains(SkillEffect.Swoop), "swoop should declare its launch scatter mechanic");
Assert(ruleset.Skills.Single(skill => skill.Id == "titchy").Effects.Contains(SkillEffect.Titchy), "titchy should declare its dodge and tackle-zone mechanic");
Assert(ruleset.Skills.Single(skill => skill.Id == "throw-team-mate").Effects.Contains(SkillEffect.ThrowTeamMate), "throw team-mate should declare its launch mechanic");
Assert(HasHook(ruleset, "throw-team-mate", GameEventKind.ThrowTeamMate, GameEventStage.BeforeEvent), "throw team-mate should declare its launch hook");
Assert(HasHook(ruleset, "decay", GameEventKind.InjuryRoll, GameEventStage.AfterRoll), "decay should declare its injury after-roll hook");
Assert(HasHook(ruleset, "regeneration", GameEventKind.InjuryRoll, GameEventStage.AfterRoll), "regeneration should declare its injury after-roll hook");
Assert(ruleset.Skills.Count >= 75, "ruleset should include the full 2020 skill and trait catalog");
Assert(ruleset.Skills.Single(skill => skill.Id == "frenzy").Compulsory, "compulsory skills should be represented in ruleset data");
Assert(ruleset.Skills.Single(skill => skill.Id == "disturbing-presence").Category == "mutation", "skill categories should be represented in ruleset data");
Assert(ruleset.Skills.Single(skill => skill.Id == "loner").Compulsory, "compulsory traits should be represented in ruleset data");
AssertThrowsInvalidData(
    () => new RosterSetValidator().Validate(rosterSet with { Rosters = [] }, ruleset),
    "roster validation should reject roster sets with no rosters");
AssertThrowsInvalidData(
    () => new RosterSetValidator().Validate(rosterSet with
    {
        Rosters =
        [
            .. rosterSet.Rosters.Select(roster => roster.Id == "human"
                ? roster with { Positions = [] }
                : roster)
        ]
    }, ruleset),
    "roster validation should reject rosters with no positions");
AssertThrowsInvalidData(
    () => new RosterSetValidator().Validate(rosterSet with
    {
        Rosters =
        [
            .. rosterSet.Rosters.Select(roster => roster.Id == "human"
                ? roster with { RosterRestrictions = ["unknown-restriction"] }
                : roster)
        ]
    }, ruleset),
    "roster validation should reject unknown roster restriction metadata");
AssertThrowsInvalidData(
    () => new RosterSetValidator().Validate(rosterSet with
    {
        Rosters =
        [
            .. rosterSet.Rosters.Select(roster => roster.Id == "human"
                ? roster with
                {
                    Positions =
                    [
                        .. roster.Positions.Select(position => position.Id == "lineman"
                            ? position with { PrimarySkillCategories = ["mystery"] }
                            : position)
                    ]
                }
                : roster)
        ]
    }, ruleset),
    "roster validation should reject unknown advancement skill categories");
AssertThrowsInvalidData(
    () => new RosterSetValidator().Validate(rosterSet with
    {
        Rosters =
        [
            .. rosterSet.Rosters.Select(roster => roster.Id == "human"
                ? roster with
                {
                    Positions =
                    [
                        .. roster.Positions.Select(position => position.Id == "lineman"
                            ? position with { Stats = null! }
                            : position)
                    ]
                }
                : roster)
        ]
    }, ruleset),
    "roster validation should reject positions with missing stats");
AssertThrowsInvalidData(
    () => new RosterSetValidator().Validate(rosterSet with
    {
        StarPlayers =
        [
            .. rosterSet.StarPlayers.Select(star => star.Id == "griff-oberwald"
                ? star with { SpecialRules = ["unknown-special-rule"] }
                : star)
        ]
    }, ruleset),
    "roster validation should reject star player eligibility rules that no roster defines");

smoke.StartSection("League creation, roster validation, and persistence");
// Fixed d16 of 1 makes the random MVP selection deterministic (always the first eligible player).
var leagueService = new LeagueService(new FixedDiceRoller(d16: [1]));
var league = leagueService.CreateLeague("Smoke League", ruleset, [rosterSet], targetTeamCount: 4);
var humanRoster = rosterSet.Rosters.Single(roster => roster.Id == "human");

league = leagueService.AddTeam(
    league,
    ruleset,
    "Smoke Humans",
    "Tester",
    humanRoster,
    [
        new("One", "lineman"),
        new("Two", "lineman"),
        new("Three", "lineman"),
        new("Four", "lineman"),
        new("Five", "lineman"),
        new("Six", "lineman"),
        new("Seven", "thrower"),
        new("Eight", "catcher"),
        new("Nine", "blitzer"),
        new("Ten", "blitzer"),
        new("Eleven", "ogre")
    ],
    rerolls: 2);

var leaguePath = Path.Combine(root, "tests", "SoloBB.Tests", "bin", "smoke-league.json");
await store.SaveLeagueAsync(leaguePath, league);
var loadedLeague = await store.LoadLeagueAsync(leaguePath);

Assert(loadedLeague.Teams.Count == 1, "saved league should round-trip with one team");

// Legacy saves stored the persistent characteristic under the old "fanFactor" key; the loader migrates it.
var legacyLeagueJson = (await File.ReadAllTextAsync(leaguePath)).Replace("\"dedicatedFans\"", "\"fanFactor\"", StringComparison.Ordinal);
var legacyLeaguePath = Path.Combine(root, "tests", "SoloBB.Tests", "bin", "smoke-legacy-league.json");
await File.WriteAllTextAsync(legacyLeaguePath, legacyLeagueJson);
var migratedLeague = await store.LoadLeagueAsync(legacyLeaguePath);
Assert(migratedLeague.Teams[0].DedicatedFans == loadedLeague.Teams[0].DedicatedFans, "legacy fanFactor saves should migrate to DedicatedFans on load");
Assert(loadedLeague.TargetTeamCount == 4, "saved league should round-trip target team count");
Assert(loadedLeague.Teams[0].Players.Count == 11, "team should round-trip with eleven players");

// Jersey numbers are auto-assigned 1..N in draft order and round-trip through persistence.
var numberedTeam = loadedLeague.Teams[0];
Assert(numberedTeam.Players.Select(player => player.Number).OrderBy(number => number).SequenceEqual(Enumerable.Range(1, 11)), "team creation should assign jersey numbers 1..N");
Assert(numberedTeam.Players.Single(player => player.Name == "One").Number == 1 && numberedTeam.Players.Single(player => player.Name == "Eleven").Number == 11, "jersey numbers should follow draft order");

// Moving a player down swaps numbers with the next player; moving the first player up is a no-op.
var jerseyPlayerOne = numberedTeam.Players.Single(player => player.Name == "One");
var jerseyPlayerTwo = numberedTeam.Players.Single(player => player.Name == "Two");
var movedDownTeam = leagueService.MovePlayer(loadedLeague, numberedTeam.Id, jerseyPlayerOne.Id, up: false).Teams[0];
Assert(movedDownTeam.Players.Single(player => player.Id == jerseyPlayerOne.Id).Number == 2 && movedDownTeam.Players.Single(player => player.Id == jerseyPlayerTwo.Id).Number == 1, "moving a player down should swap jersey numbers with the next player");
var boundaryTeam = leagueService.MovePlayer(loadedLeague, numberedTeam.Id, jerseyPlayerOne.Id, up: true).Teams[0];
Assert(boundaryTeam.Players.Single(player => player.Id == jerseyPlayerOne.Id).Number == 1, "moving the first player up should be a no-op");
Assert(loadedLeague.Teams[0].TeamValue == 855_000, "team value should round-trip");

var awayLeague = leagueService.CreateLeague("Away Smoke League", ruleset, [rosterSet]);
awayLeague = leagueService.AddTeam(
    awayLeague,
    ruleset,
    "Smoke Orcs",
    "Tester",
    rosterSet.Rosters.Single(roster => roster.Id == "orc"),
    Enumerable.Range(1, 11).Select(index => new PlayerDraftPick($"Orc Lineman {index}", "lineman")),
    rerolls: 2);

var benchLeague = leagueService.CreateLeague("Bench Smoke League", ruleset, [rosterSet]);
benchLeague = leagueService.AddTeam(
    benchLeague,
    ruleset,
    "Smoke Bench",
    "Tester",
    humanRoster,
    Enumerable.Range(1, 12).Select(index => new PlayerDraftPick($"Bench Lineman {index}", "lineman")),
    rerolls: 2);

Assert(benchLeague.Teams[0].Players.Count == 12, "league teams should allow more than eleven players");
Assert(benchLeague.Teams[0].TeamValue == 700_000, "team value should include players and rerolls");

var fullRosterLeague = leagueService.CreateLeague("Full Roster League", ruleset, [rosterSet]);
fullRosterLeague = leagueService.AddTeam(
    fullRosterLeague,
    ruleset,
    "Smoke Full Roster",
    "Tester",
    humanRoster,
    Enumerable.Range(1, 16).Select(index => new PlayerDraftPick($"Full Roster Lineman {index}", "lineman")),
    rerolls: 0);

Assert(fullRosterLeague.Teams[0].Players.Count == 16, "league teams should allow sixteen-player rosters");

var fanFactorLeague = leagueService.CreateLeague("Fan Factor League", ruleset, [rosterSet]);
fanFactorLeague = leagueService.AddTeam(
    fanFactorLeague,
    ruleset,
    "Smoke Fans",
    "Tester",
    humanRoster,
    Enumerable.Range(1, 11).Select(index => new PlayerDraftPick($"Fan Lineman {index}", "lineman")),
    rerolls: 0,
    dedicatedFans:1);

Assert(fanFactorLeague.Teams[0].Treasury == 450_000, "fan factor one should be free");
Assert(fanFactorLeague.Teams[0].TeamValue == 550_000, "team value should include free fan factor correctly");

var paidFanFactorLeague = leagueService.CreateLeague("Paid Fan Factor League", ruleset, [rosterSet]);
paidFanFactorLeague = leagueService.AddTeam(
    paidFanFactorLeague,
    ruleset,
    "Smoke Paid Fans",
    "Tester",
    humanRoster,
    Enumerable.Range(1, 11).Select(index => new PlayerDraftPick($"Paid Fan Lineman {index}", "lineman")),
    rerolls: 0,
    dedicatedFans:2);

Assert(paidFanFactorLeague.Teams[0].Treasury == 440_000, "fan factor above one should cost 10,000 gp per point");
Assert(paidFanFactorLeague.Teams[0].TeamValue == 560_000, "team value should include paid fan factor");

var staffLeague = leagueService.CreateLeague("Staff League", ruleset, [rosterSet]);
staffLeague = leagueService.AddTeam(
    staffLeague,
    ruleset,
    "Smoke Staff",
    "Tester",
    humanRoster,
    Enumerable.Range(1, 11).Select(index => new PlayerDraftPick($"Staff Lineman {index}", "lineman")),
    rerolls: 0,
    dedicatedFans:1,
    cheerleaders: 2,
    assistantCoaches: 1,
    apothecaries: 1);

Assert(staffLeague.Teams[0].TeamValue == 630_000, "team value should include cheerleaders, assistant coaches, and apothecaries");
Assert(staffLeague.Teams[0].Treasury == 370_000, "staff purchases should reduce treasury");

var scheduledLeague = leagueService.CreateLeague("Scheduled League", ruleset, [rosterSet], targetTeamCount: 4);
for (var teamIndex = 1; teamIndex <= 4; teamIndex++)
{
    scheduledLeague = leagueService.AddTeam(
        scheduledLeague,
        ruleset,
        $"Schedule Team {teamIndex}",
        "Scheduler",
        humanRoster,
        Enumerable.Range(1, 11).Select(playerIndex => new PlayerDraftPick($"Schedule {teamIndex} Lineman {playerIndex}", "lineman")),
        rerolls: 0);
}

scheduledLeague = leagueService.CreateSeason(scheduledLeague);
var scheduledSeason = scheduledLeague.Seasons.Single();
var scheduledWeeks = scheduledSeason.Schedule.GroupBy(match => match.Week).OrderBy(group => group.Key).ToArray();

Assert(scheduledWeeks.Length == 6, "double round-robin should create (teams - 1) * 2 weeks");
Assert(scheduledSeason.Schedule.Count == 12, "four-team double round-robin should create twelve matches");
Assert(scheduledWeeks.All(group => group.Count() == 2), "each week should have two games for four teams");

var scheduledPairs = scheduledSeason.Schedule
    .GroupBy(match => string.Join(":", new[] { match.HomeTeamId, match.AwayTeamId }.Order()))
    .ToArray();

Assert(scheduledPairs.Length == 6, "each team pair should appear once as a pair");
Assert(scheduledPairs.All(group => group.Count() == 2), "each team pair should play twice");
Assert(scheduledPairs.All(group => group.Select(match => match.HomeTeamId).Distinct().Count() == 2), "each pair should swap home and away");

foreach (var teamId in scheduledLeague.Teams.Select(team => team.Id))
{
    var opponentsByWeek = scheduledSeason.Schedule
        .Where(match => match.HomeTeamId == teamId || match.AwayTeamId == teamId)
        .OrderBy(match => match.Week)
        .Select(match => match.HomeTeamId == teamId ? match.AwayTeamId : match.HomeTeamId)
        .ToArray();

    Assert(!opponentsByWeek.Zip(opponentsByWeek.Skip(1), (current, next) => current == next).Any(repeated => repeated), "teams should not play the same opponent twice in a row");
}

var firstHalfSequence = scheduledWeeks.Take(3).Select(group => string.Join(",", group.Select(match => string.Join(":", new[] { match.HomeTeamId, match.AwayTeamId }.Order())).Order())).ToArray();
var secondHalfSequence = scheduledWeeks.Skip(3).Select(group => string.Join(",", group.Select(match => string.Join(":", new[] { match.HomeTeamId, match.AwayTeamId }.Order())).Order())).ToArray();

Assert(!firstHalfSequence.SequenceEqual(secondHalfSequence), "second half schedule should not repeat the first-half sequence in the same order");

smoke.StartSection("Post-match campaign lifecycle");
var campaignWeekOne = scheduledLeague.Seasons.Single().Schedule.Where(match => match.Week == 1).ToArray();
var firstCampaignMatch = campaignWeekOne[0];
var secondCampaignMatch = campaignWeekOne[1];
var campaignHomeTeam = scheduledLeague.Teams.First(team => team.Id == firstCampaignMatch.HomeTeamId);
var campaignAwayTeam = scheduledLeague.Teams.First(team => team.Id == firstCampaignMatch.AwayTeamId);
var campaignScorer = campaignHomeTeam.Players[0];
var returningPlayer = campaignHomeTeam.Players[1];
var injuredAwayPlayer = campaignAwayTeam.Players[0];
scheduledLeague = scheduledLeague with
{
    Teams = scheduledLeague.Teams
        .Select(team => team.Id == campaignHomeTeam.Id
            ? team with
            {
                Players = team.Players
                    .Select(player => player.Id == returningPlayer.Id ? player with { Status = PlayerStatus.MissNextGame } : player)
                    .ToArray()
            }
            : team)
        .ToArray()
};

var completedCampaignMatch = new MatchState
{
    Id = Guid.NewGuid(),
    RulesetId = ruleset.Id,
    HomeTeamId = campaignHomeTeam.Id,
    AwayTeamId = campaignAwayTeam.Id,
    Phase = MatchPhase.Complete,
    HomeScore = 2,
    AwayScore = 1,
    HomeFanFactor = 4,
    AwayFanFactor = 3,
    HomeTreasurySpent = 100_000,
    PlayerAwards =
    [
        new MatchPlayerAward
        {
            TeamId = campaignHomeTeam.Id,
            PlayerId = campaignScorer.Id,
            Kind = MatchPlayerAwardKind.Touchdown,
            StarPlayerPoints = 3
        },
        new MatchPlayerAward
        {
            TeamId = campaignHomeTeam.Id,
            PlayerId = returningPlayer.Id,
            VictimPlayerId = injuredAwayPlayer.Id,
            Kind = MatchPlayerAwardKind.Casualty,
            StarPlayerPoints = 2
        }
    ],
    Placements =
    [
        new PlayerPlacement
        {
            TeamId = campaignAwayTeam.Id,
            PlayerId = injuredAwayPlayer.Id,
            State = PlayerPitchState.Casualty,
            Casualty = new CasualtyRoll { Roll = 8, Result = CasualtyResult.SeriouslyHurt }
        }
    ]
};

var afterFirstCampaignMatch = leagueService.CompleteScheduledMatch(scheduledLeague, ruleset, firstCampaignMatch.Id, completedCampaignMatch);
var firstCampaignResult = afterFirstCampaignMatch.Seasons.Single().Schedule.Single(match => match.Id == firstCampaignMatch.Id).Result
    ?? throw new InvalidOperationException("Completed campaign match should have a result.");
var updatedCampaignHome = afterFirstCampaignMatch.Teams.Single(team => team.Id == campaignHomeTeam.Id);
var updatedCampaignAway = afterFirstCampaignMatch.Teams.Single(team => team.Id == campaignAwayTeam.Id);

Assert(firstCampaignResult.HomeScore == 2 && firstCampaignResult.AwayScore == 1, "post-match should record the completed scheduled result");
Assert(firstCampaignResult.PlayerAwards.Any(award => award.Kind == MatchPlayerAwardKind.MostValuablePlayer), "post-match should add MVP awards");
var enrichedTouchdownAward = firstCampaignResult.PlayerAwards.Single(award => award.Kind == MatchPlayerAwardKind.Touchdown);
var enrichedCasualtyAward = firstCampaignResult.PlayerAwards.Single(award => award.Kind == MatchPlayerAwardKind.Casualty);
Assert(enrichedTouchdownAward.PlayerName == campaignScorer.Name && enrichedTouchdownAward.TeamName == campaignHomeTeam.Name, "post-match should enrich touchdown SPP awards with player and team names");
Assert(enrichedCasualtyAward.PlayerName == returningPlayer.Name && enrichedCasualtyAward.VictimPlayerName == injuredAwayPlayer.Name && enrichedCasualtyAward.CasualtyResult == CasualtyResult.SeriouslyHurt, "post-match should enrich casualty SPP awards with victim names and casualty results");
Assert(afterFirstCampaignMatch.Seasons.Single().CurrentWeek == 1, "league week should not advance until every game in the week is complete");
Assert(updatedCampaignHome.Treasury == campaignHomeTeam.Treasury - 100_000 + firstCampaignResult.HomeWinnings, "post-match should apply pre-game treasury spend and winnings");
// BB2020 winnings: Fan Attendance (4 + 3 = 7) / 2 = 3, plus touchdowns scored, multiplied by 10,000.
Assert(firstCampaignResult.HomeWinnings == 50_000, "BB2020 winnings should be (Fan Attendance / 2 + touchdowns) * 10,000 for the home team");
Assert(firstCampaignResult.AwayWinnings == 40_000, "BB2020 winnings should be (Fan Attendance / 2 + touchdowns) * 10,000 for the away team");
Assert(updatedCampaignHome.Players.Single(player => player.Id == campaignScorer.Id).StarPlayerPoints == 7, "post-match should apply touchdown and MVP SPP");
Assert(updatedCampaignHome.Players.Single(player => player.Id == returningPlayer.Id).StarPlayerPoints == 2, "post-match should apply casualty SPP to the credited player");

// BB2020 end-of-season redraft. The home team played one fixture and won it.
var redraftBudget = leagueService.CalculateRedraftBudget(afterFirstCampaignMatch, campaignHomeTeam.Id);
Assert(redraftBudget.FixturesPlayed == 1 && redraftBudget.FixturesWon == 1 && redraftBudget.FixturesDrawn == 0, "redraft budget should reflect the team's season record");
Assert(redraftBudget.Base == 1_000_000, "redraft base budget should be 1,000,000 gp");
Assert(redraftBudget.Subtotal == 1_000_000 + updatedCampaignHome.Treasury + 20_000 + 20_000, "redraft subtotal should add treasury plus 20k per fixture played and 20k per win");
Assert(redraftBudget.Total == Math.Min(redraftBudget.Subtotal, 1_300_000), "redraft budget should be capped at 1,300,000 gp");

var redraftRetainedIds = updatedCampaignHome.Players.Where(player => player.Status == PlayerStatus.Available).Select(player => player.Id).ToArray();
var redraftedLeague = leagueService.RedraftTeam(afterFirstCampaignMatch, ruleset, humanRoster, campaignHomeTeam.Id, redraftRetainedIds, [], rerolls: 0, cheerleaders: 0, assistantCoaches: 0, apothecaries: 0);
var redraftedTeam = redraftedLeague.Teams.Single(team => team.Id == campaignHomeTeam.Id);
Assert(redraftedTeam.Players.Count == redraftRetainedIds.Length, "redraft should retain the chosen players");
Assert(redraftedTeam.Players.All(player => player.Status == PlayerStatus.Available), "redrafted players should be available for the new season");
Assert(redraftedTeam.DedicatedFans == updatedCampaignHome.DedicatedFans, "Dedicated Fans should carry over through the redraft");
Assert(redraftedTeam.Rerolls == 0, "redraft should rebuild rerolls from the chosen amount");
var redraftFansCost = Math.Max(0, redraftedTeam.DedicatedFans - 1) * 10_000;
var redraftRetainedCost = (redraftedTeam.TeamValue - redraftFansCost) + (20_000 * redraftRetainedIds.Length);
Assert(redraftedTeam.Treasury == redraftBudget.Total - redraftRetainedCost, "redraft should charge each retained player their value plus a 20,000 agent fee and bank the remainder");

var nextSeasonLeague = leagueService.StartNewSeason(redraftedLeague, "Season 2");
Assert(nextSeasonLeague.Seasons.Count == afterFirstCampaignMatch.Seasons.Count + 1, "StartNewSeason should append a new season");
Assert(nextSeasonLeague.Seasons.Last().Schedule.Count > 0, "the new season should have a generated schedule");
Assert(updatedCampaignHome.Players.Single(player => player.Id == returningPlayer.Id).Status == PlayerStatus.Available, "post-match should clear old Miss Next Game status after the missed match");
Assert(updatedCampaignHome.DedicatedFans == campaignHomeTeam.DedicatedFans + 1, "post-match win should improve the team's Dedicated Fans");
Assert(updatedCampaignAway.Players.Single(player => player.Id == injuredAwayPlayer.Id).Status == PlayerStatus.MissNextGame, "post-match should apply current-match casualty roster status");

var selectedAdvancementLeague = leagueService.PurchaseSelectedSkillAdvancement(afterFirstCampaignMatch, ruleset, humanRoster, campaignHomeTeam.Id, campaignScorer.Id, "block");
var selectedAdvancedPlayer = selectedAdvancementLeague.Teams.Single(team => team.Id == campaignHomeTeam.Id).Players.Single(player => player.Id == campaignScorer.Id);
Assert(selectedAdvancedPlayer.Skills.Contains("block"), "selected advancement should add the purchased skill");
Assert(selectedAdvancedPlayer.StarPlayerPoints == 1, "chosen primary advancement should spend 6 SPP (BB2020)");
Assert(selectedAdvancementLeague.Teams.Single(team => team.Id == campaignHomeTeam.Id).TeamValue == updatedCampaignHome.TeamValue + 20_000, "primary skill advancement should increase team value");

var randomReadyLeague = afterFirstCampaignMatch with
{
    Teams = afterFirstCampaignMatch.Teams
        .Select(team => team.Id == campaignHomeTeam.Id
            ? team with
            {
                Players = team.Players
                    .Select(player => player.Id == returningPlayer.Id ? player with { StarPlayerPoints = 6 } : player)
                    .ToArray()
            }
            : team)
        .ToArray()
};
// BB2020 random selection rolls 2D6 (a non-double draws from the primary table) and then picks a
// skill at random via the dice rather than always taking the alphabetically-first option.
var randomPrimaryService = new LeagueService(new FixedDiceRoller(d6: [1, 2], d16: [1]));
var randomAdvancementLeague = randomPrimaryService.PurchaseRandomSkillAdvancement(randomReadyLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id);
var randomAdvancedPlayer = randomAdvancementLeague.Teams.Single(team => team.Id == campaignHomeTeam.Id).Players.Single(player => player.Id == returningPlayer.Id);
Assert(randomAdvancedPlayer.StarPlayerPoints == 3 && randomAdvancedPlayer.Skills.Count == returningPlayer.Skills.Count + 1, "random primary advancement should add an eligible skill and spend 3 SPP (BB2020)");
Assert(randomAdvancedPlayer.Skills.Contains("block"), "non-double random primary should draw from the lineman's primary (general) table at the rolled index");

// A different index roll selects a different skill, proving the pick is dice-driven, not alphabetical.
var randomAltPlayer = new LeagueService(new FixedDiceRoller(d6: [1, 2], d16: [2]))
    .PurchaseRandomSkillAdvancement(randomReadyLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id)
    .Teams.Single(team => team.Id == campaignHomeTeam.Id).Players.Single(player => player.Id == returningPlayer.Id);
Assert(!randomAltPlayer.Skills.Contains("block") && randomAltPlayer.Skills.Count == returningPlayer.Skills.Count + 1, "a different random index roll should select a different skill");

// Rolling a double lets the player take a skill from any available category. A lineman (primary
// General; secondary Agility/Passing/Strength) can then gain a Passing skill — but because it was
// bought on the random *primary* table, it is still priced as a random primary (3 SPP, +10k value)
// rather than by the chosen skill's secondary category.
var randomDoubleLeague = new LeagueService(new FixedDiceRoller(d6: [3, 3], d16: [1]))
    .PurchaseRandomSkillAdvancement(randomReadyLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id);
var randomDoubleTeam = randomDoubleLeague.Teams.Single(team => team.Id == campaignHomeTeam.Id);
var randomDoublePlayer = randomDoubleTeam.Players.Single(player => player.Id == returningPlayer.Id);
Assert(randomDoublePlayer.Skills.Contains("accurate"), "a doubles roll should expand random selection to secondary categories");
Assert(randomDoublePlayer.StarPlayerPoints == 3, "a doubles skill bought on the random primary table should still cost 3 SPP (BB2020)");
Assert(randomDoubleTeam.TeamValue == updatedCampaignHome.TeamValue + 10_000, "a random primary advancement should add 10k value even when doubles selects a secondary-category skill");

// A random *secondary* roll is billed as a random secondary (6 SPP, +20k) regardless of the skill.
var randomSecondaryLeague = new LeagueService(new FixedDiceRoller(d6: [1, 2], d16: [1]))
    .PurchaseRandomSkillAdvancement(randomReadyLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id, secondary: true);
var randomSecondaryTeam = randomSecondaryLeague.Teams.Single(team => team.Id == campaignHomeTeam.Id);
var randomSecondaryPlayer = randomSecondaryTeam.Players.Single(player => player.Id == returningPlayer.Id);
Assert(randomSecondaryPlayer.StarPlayerPoints == 0, "a random secondary advancement should cost 6 SPP (BB2020)");
Assert(randomSecondaryTeam.TeamValue == updatedCampaignHome.TeamValue + 20_000, "a random secondary advancement should add 20k value");

// Characteristic improvements (BB2020): a flat 18 SPP with a D16 deciding which characteristics may
// be raised. MA/AV/ST increase the stat; AG/PA lower the target number.
var characteristicReadyLeague = afterFirstCampaignMatch with
{
    Teams = afterFirstCampaignMatch.Teams
        .Select(team => team.Id == campaignHomeTeam.Id
            ? team with
            {
                Players = team.Players
                    .Select(player => player.Id == returningPlayer.Id ? player with { StarPlayerPoints = 18 } : player)
                    .ToArray()
            }
            : team)
        .ToArray()
};

// A roll of 1-7 permits Movement or Armour. Choosing Movement adds +1 MA, spends 18 SPP, +20k TV.
var movementImprovedLeague = new LeagueService(new FixedDiceRoller(d16: [5]))
    .PurchaseCharacteristicAdvancement(characteristicReadyLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id, PlayerCharacteristic.Movement);
var movementImprovedTeam = movementImprovedLeague.Teams.Single(team => team.Id == campaignHomeTeam.Id);
var movementImprovedPlayer = movementImprovedTeam.Players.Single(player => player.Id == returningPlayer.Id);
Assert(movementImprovedPlayer.Stats.Movement == returningPlayer.Stats.Movement + 1 && movementImprovedPlayer.StarPlayerPoints == 0, "characteristic improvement should add +1 MA and spend 18 SPP (BB2020)");
Assert(movementImprovedPlayer.CharacteristicImprovements.Count == returningPlayer.CharacteristicImprovements.Count + 1 && movementImprovedPlayer.CharacteristicImprovements.Contains(PlayerCharacteristic.Movement), "a characteristic improvement should be recorded against the player");
Assert(movementImprovedTeam.TeamValue == updatedCampaignHome.TeamValue + 20_000, "a +1 MA improvement should add 20k to team value");

// A roll of 10-12 permits Agility, which lowers the AG target by 1 and adds 40k to team value.
var agilityImprovedLeague = new LeagueService(new FixedDiceRoller(d16: [11]))
    .PurchaseCharacteristicAdvancement(characteristicReadyLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id, PlayerCharacteristic.Agility);
var agilityImprovedTeam = agilityImprovedLeague.Teams.Single(team => team.Id == campaignHomeTeam.Id);
var agilityImprovedPlayer = agilityImprovedTeam.Players.Single(player => player.Id == returningPlayer.Id);
Assert(agilityImprovedPlayer.Stats.Agility == returningPlayer.Stats.Agility - 1, "characteristic AG improvement should lower the AG target by 1 (BB2020)");
Assert(agilityImprovedTeam.TeamValue == updatedCampaignHome.TeamValue + 40_000, "a +1 AG improvement should add 40k to team value");

// The D16 table is enforced: a roll of 1-7 does not allow improving Strength.
AssertThrows(
    () => new LeagueService(new FixedDiceRoller(d16: [3]))
        .PurchaseCharacteristicAdvancement(characteristicReadyLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id, PlayerCharacteristic.Strength),
    "a low characteristic roll should not permit a Strength increase");

// The improvement requires the full 18 SPP (randomReadyLeague granted the player only 6).
AssertThrows(
    () => new LeagueService(new FixedDiceRoller(d16: [16]))
        .PurchaseCharacteristicAdvancement(randomReadyLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id, PlayerCharacteristic.Strength),
    "a characteristic improvement should require the full 18 SPP");

// BB2020 hard cap: a player may never exceed six career advancements. A lineman holding six gained
// skills (with SPP to spare) cannot take a seventh advancement.
var sixSkillLeague = afterFirstCampaignMatch with
{
    Teams = afterFirstCampaignMatch.Teams
        .Select(team => team.Id == campaignHomeTeam.Id
            ? team with
            {
                Players = team.Players
                    .Select(player => player.Id == returningPlayer.Id
                        ? player with { Skills = ["block", "dauntless", "dirty-player", "fend", "frenzy", "kick"], StarPlayerPoints = 50 }
                        : player)
                    .ToArray()
            }
            : team)
        .ToArray()
};
AssertThrows(
    () => leagueService.PurchaseSelectedSkillAdvancement(sixSkillLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id, "pro"),
    "a player at the six-advancement cap should not gain another skill");

// Characteristic improvements count toward the six-advancement total: five skills plus one
// characteristic improvement is already six, so a further skill is rejected.
var fiveSkillsOneCharLeague = afterFirstCampaignMatch with
{
    Teams = afterFirstCampaignMatch.Teams
        .Select(team => team.Id == campaignHomeTeam.Id
            ? team with
            {
                Players = team.Players
                    .Select(player => player.Id == returningPlayer.Id
                        ? player with { Skills = ["block", "dauntless", "dirty-player", "fend", "frenzy"], CharacteristicImprovements = [PlayerCharacteristic.Movement], StarPlayerPoints = 50 }
                        : player)
                    .ToArray()
            }
            : team)
        .ToArray()
};
AssertThrows(
    () => leagueService.PurchaseSelectedSkillAdvancement(fiveSkillsOneCharLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id, "pro"),
    "characteristic improvements should count toward the six-advancement cap");

// Any single characteristic may only be improved twice. A player who has already raised Strength
// twice cannot raise it a third time...
var twiceStrengthLeague = afterFirstCampaignMatch with
{
    Teams = afterFirstCampaignMatch.Teams
        .Select(team => team.Id == campaignHomeTeam.Id
            ? team with
            {
                Players = team.Players
                    .Select(player => player.Id == returningPlayer.Id
                        ? player with { CharacteristicImprovements = [PlayerCharacteristic.Strength, PlayerCharacteristic.Strength], StarPlayerPoints = 18 }
                        : player)
                    .ToArray()
            }
            : team)
        .ToArray()
};
AssertThrows(
    () => new LeagueService(new FixedDiceRoller(d16: [16]))
        .PurchaseCharacteristicAdvancement(twiceStrengthLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id, PlayerCharacteristic.Strength),
    "a characteristic that has been improved twice should not be improved again");

// ...but may still improve a different characteristic (the cap is per-characteristic, not total).
var twiceStrengthThenMovement = new LeagueService(new FixedDiceRoller(d16: [5]))
    .PurchaseCharacteristicAdvancement(twiceStrengthLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id, PlayerCharacteristic.Movement)
    .Teams.Single(team => team.Id == campaignHomeTeam.Id).Players.Single(player => player.Id == returningPlayer.Id);
Assert(twiceStrengthThenMovement.Stats.Movement == returningPlayer.Stats.Movement + 1 && twiceStrengthThenMovement.CharacteristicImprovements.Count == 3, "the twice-per-characteristic cap should not block a different characteristic");

// BB2020-accurate two-step UI flow: RollCharacteristicAdvancement rolls the D16 and reports the legal
// choices, then ApplyCharacteristicAdvancement commits the chosen one against that same roll.
var twoStepService = new LeagueService(new FixedDiceRoller(d16: [5]));
var characteristicRoll = twoStepService.RollCharacteristicAdvancement(characteristicReadyLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id);
Assert(characteristicRoll.Roll == 5 && characteristicRoll.Cost == 18, "rolling a characteristic advancement reports the D16 result and its 18 SPP cost");
Assert(characteristicRoll.Options.Contains(PlayerCharacteristic.Movement) && characteristicRoll.Options.Contains(PlayerCharacteristic.Armor) && !characteristicRoll.Options.Contains(PlayerCharacteristic.Strength), "a roll of 1-7 should offer only Movement and Armour");
var twoStepImproved = twoStepService
    .ApplyCharacteristicAdvancement(characteristicReadyLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id, characteristicRoll.Roll, PlayerCharacteristic.Movement)
    .Teams.Single(team => team.Id == campaignHomeTeam.Id).Players.Single(player => player.Id == returningPlayer.Id);
Assert(twoStepImproved.Stats.Movement == returningPlayer.Stats.Movement + 1 && twoStepImproved.StarPlayerPoints == 0, "applying the rolled characteristic advancement should raise MA and spend 18 SPP");
AssertThrows(
    () => twoStepService.ApplyCharacteristicAdvancement(characteristicReadyLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id, 5, PlayerCharacteristic.Strength),
    "applying a characteristic the rolled table does not permit should be rejected");

// BB2020 fallback: instead of a rolled characteristic the player may take a Chosen Secondary skill,
// still spending the full 18 SPP but gaining the Chosen Secondary (+40k) value.
var returningPosition = humanRoster.Positions.First(position => string.Equals(position.Id, returningPlayer.PositionId, StringComparison.OrdinalIgnoreCase));
var fallbackSecondary = ruleset.Skills.First(skill => !skill.DataOnly && !skill.Compulsory
    && returningPosition.SecondarySkillCategories.Contains(skill.Category, StringComparer.OrdinalIgnoreCase)
    && !returningPlayer.Skills.Contains(skill.Id, StringComparer.OrdinalIgnoreCase));
var fallbackLeague = twoStepService.ApplyCharacteristicSecondarySkill(characteristicReadyLeague, ruleset, humanRoster, campaignHomeTeam.Id, returningPlayer.Id, fallbackSecondary.Id);
var fallbackTeam = fallbackLeague.Teams.Single(team => team.Id == campaignHomeTeam.Id);
var fallbackPlayer = fallbackTeam.Players.Single(player => player.Id == returningPlayer.Id);
Assert(fallbackPlayer.Skills.Contains(fallbackSecondary.Id) && fallbackPlayer.StarPlayerPoints == 0, "the characteristic-to-secondary-skill fallback should add the skill and spend the full 18 SPP");
Assert(fallbackTeam.TeamValue == updatedCampaignHome.TeamValue + 40_000, "the secondary-skill fallback should add the Chosen Secondary 40k value");

// BB2020 player titles are derived from the number of advancements taken (one band each).
Assert(LeagueService.PlayerTitle(humanRoster, returningPlayer) == "Rookie", "a player with no advancements is a Rookie");
Assert(LeagueService.PlayerTitle(humanRoster, returningPlayer with { Skills = ["block"] }) == "Experienced", "one advancement makes a player Experienced");
Assert(LeagueService.PlayerTitle(humanRoster, returningPlayer with { Skills = ["block", "dauntless"], CharacteristicImprovements = [PlayerCharacteristic.Movement] }) == "Emerging Star", "three advancements (skills plus a characteristic) makes an Emerging Star");
Assert(LeagueService.PlayerTitle(humanRoster, returningPlayer with { Skills = ["block", "dauntless", "dirty-player", "fend", "frenzy", "kick"] }) == "Legend", "six advancements makes a player a Legend");

var secondCampaignHome = scheduledLeague.Teams.First(team => team.Id == secondCampaignMatch.HomeTeamId);
var secondCampaignAway = scheduledLeague.Teams.First(team => team.Id == secondCampaignMatch.AwayTeamId);
var completedSecondCampaignMatch = new MatchState
{
    Id = Guid.NewGuid(),
    RulesetId = ruleset.Id,
    HomeTeamId = secondCampaignHome.Id,
    AwayTeamId = secondCampaignAway.Id,
    Phase = MatchPhase.Complete,
    HomeScore = 0,
    AwayScore = 0
};
var afterWeekComplete = leagueService.CompleteScheduledMatch(afterFirstCampaignMatch, ruleset, secondCampaignMatch.Id, completedSecondCampaignMatch);
Assert(afterWeekComplete.Seasons.Single().CurrentWeek == 2, "league week should advance after all current-week games have results");

smoke.StartSection("Match creation, setup, and persistence");
var matchService = new MatchService();
var preGameService = new PreGameService();
var match = matchService.CreateHotseatMatch(ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
var benchMatch = matchService.CreateHotseatMatch(ruleset, benchLeague.Teams[0], awayLeague.Teams[0]);
var depletedTeam = benchLeague.Teams[0] with { Players = benchLeague.Teams[0].Players.Take(3).ToArray() };
var depletedMatch = matchService.CreateHotseatMatch(ruleset, depletedTeam, awayLeague.Teams[0]);
var richerAwayTeam = awayLeague.Teams[0] with { TeamValue = loadedLeague.Teams[0].TeamValue + 200_000 };
var richerAwayTeamWithTreasury = richerAwayTeam with { Treasury = 50_000 };
var preGameSummary = preGameService.BuildSummary(ruleset, rosterSet, loadedLeague.Teams[0], richerAwayTeam);
var bribePlan = preGameService.CreatePlan(ruleset, rosterSet, loadedLeague.Teams[0], richerAwayTeam, homeBribes: 2, awayBribes: 0);
var higherTvTreasuryPlan = preGameService.CreatePlan(ruleset, rosterSet, loadedLeague.Teams[0], richerAwayTeamWithTreasury, homeBribes: 0, awayBribes: 0, awayTreasurySpent: 50_000);
var preparedBribeMatch = preGameService.PrepareMatch(ruleset, rosterSet, loadedLeague.Teams[0], richerAwayTeam, bribePlan);
var inducedMatch = matchService.CreateHotseatMatch(ruleset, preparedBribeMatch.HomeTeam, preparedBribeMatch.AwayTeam, preparedBribeMatch.Inducements.Home, preparedBribeMatch.Inducements.Away);
var preparedDepletedMatch = preGameService.PrepareMatch(ruleset, rosterSet, depletedTeam, awayLeague.Teams[0]);
var matchPath = Path.Combine(root, "tests", "SoloBB.Tests", "bin", "smoke-match.json");
await store.SaveMatchAsync(matchPath, match);
var loadedMatch = await store.LoadMatchAsync(matchPath);

Assert(benchMatch.Placements.Count == 23, "matches should accept teams with bench players");
Assert(depletedMatch.Placements.Count == 14, "matches should accept teams with the three-player minimum");
Assert(preGameSummary.Home.PettyCash == 200_000 && preGameSummary.Away.PettyCash == 0, "pre-game should award petty cash to the lower-value team");
Assert(higherTvTreasuryPlan.Home.PettyCash == 250_000 && higherTvTreasuryPlan.Away.PettyCash == 0, "higher-TV treasury spend should increase lower-TV petty cash");
var missingBlitzer = loadedLeague.Teams[0].Players.First(player => player.PositionId == "blitzer");
var teamWithMissingAdvancedBlitzer = loadedLeague.Teams[0] with
{
    TeamValue = loadedLeague.Teams[0].TeamValue + 20_000,
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == missingBlitzer.Id
            ? player with { Status = PlayerStatus.MissNextGame, Skills = [.. player.Skills, "guard"] }
            : player)
        .ToArray()
};
var missingBlitzerSummary = preGameService.BuildSummary(ruleset, rosterSet, teamWithMissingAdvancedBlitzer, richerAwayTeam);
Assert(missingBlitzerSummary.Home.TeamValue == loadedLeague.Teams[0].TeamValue - 35_000, "current team value should remove an unavailable advanced Blitzer and add a base-cost journeyman");
Assert(missingBlitzerSummary.Home.PettyCash == 235_000, "petty cash should use current team value after unavailable players and journeymen");
var missingBlitzerPlan = preGameService.CreateDefaultPlan(ruleset, rosterSet, teamWithMissingAdvancedBlitzer, richerAwayTeam);
Assert(missingBlitzerPlan.Home.PettyCash == 235_000, "roster-aware plan creation should use current team value");
Assert(preGameSummary.Home.JourneymenNeeded == 0, "full teams should not need journeymen");
Assert(preparedDepletedMatch.Summary.Home.JourneymenNeeded == 8, "pre-game should identify journeymen needed to reach eleven available players");
Assert(preparedDepletedMatch.HomeTeam.Players.Count == 11, "pre-game should add temporary journeymen to the match team");
Assert(preparedDepletedMatch.HomeTeam.Players.Count(player => player.Injuries.Contains("journeyman")) == 8, "temporary journeymen should be marked on the match roster");
Assert(preparedDepletedMatch.HomeTeam.Players.Where(player => player.Injuries.Contains("journeyman")).All(player => player.Skills.Contains("loner")), "journeymen should have Loner");
Assert(preparedDepletedMatch.HomeTeam.Players.Select(player => player.Number).Distinct().Count() == 11 && preparedDepletedMatch.HomeTeam.Players.Where(player => player.Injuries.Contains("journeyman")).All(player => player.Number > 3), "match-only journeymen should get distinct jersey numbers continuing after the roster");
Assert(inducedMatch.HomeBribesRemaining == 2 && inducedMatch.AwayBribesRemaining == 0, "purchased inducement bribes should be available in match state");
Assert(preGameSummary.Home.AvailableInducements.Any(inducement => inducement.Id == "bloodweiser-keg" && inducement.MaxCount == 2), "pre-game summary should expose the broader inducement catalog");
var staffInducementPlan = preGameService.CreatePlan(
    ruleset,
    rosterSet,
    loadedLeague.Teams[0],
    richerAwayTeam,
    homeBribes: 0,
    awayBribes: 0,
    homeInducements:
    [
        new SelectedInducement { InducementId = "extra-team-training", Count = 1 },
        new SelectedInducement { InducementId = "temp-agency-cheerleader", Count = 1 },
        new SelectedInducement { InducementId = "part-time-assistant-coach", Count = 1 }
    ]);
var preparedStaffInducementMatch = preGameService.PrepareMatch(ruleset, rosterSet, loadedLeague.Teams[0], richerAwayTeam, staffInducementPlan);
var staffInducedMatch = matchService.CreateHotseatMatch(ruleset, preparedStaffInducementMatch.HomeTeam, preparedStaffInducementMatch.AwayTeam, preparedStaffInducementMatch.Inducements.Home, preparedStaffInducementMatch.Inducements.Away);
Assert(staffInducedMatch.HomeTeamRerolls == loadedLeague.Teams[0].Rerolls + 1, "extra team training should add a temporary team reroll");
Assert(staffInducedMatch.HomeCheerleaders == loadedLeague.Teams[0].Cheerleaders + 1, "temp agency cheerleaders should add temporary cheerleaders");
Assert(staffInducedMatch.HomeAssistantCoaches == loadedLeague.Teams[0].AssistantCoaches + 1, "part-time assistant coaches should add temporary assistant coaches");
var fixedInducementOpponent = richerAwayTeam with { TeamValue = loadedLeague.Teams[0].TeamValue + 500_000 };
var fixedInducementService = new PreGameService();
var fixedInducementPlan = fixedInducementService.CreatePlan(
    ruleset,
    rosterSet,
    loadedLeague.Teams[0],
    fixedInducementOpponent,
    homeBribes: 0,
    awayBribes: 0,
    homeInducements:
    [
        new SelectedInducement { InducementId = "weather-mage", Count = 1 },
        new SelectedInducement { InducementId = "bloodweiser-keg", Count = 1 },
        new SelectedInducement { InducementId = "special-play", Count = 1 },
        new SelectedInducement { InducementId = "halfling-master-chef", Count = 1 }
    ]);
var preparedFixedInducements = fixedInducementService.PrepareMatch(ruleset, rosterSet, loadedLeague.Teams[0], fixedInducementOpponent, fixedInducementPlan);
var fixedInducementMatch = new MatchService(new FixedDiceRoller(d6: [4, 5, 1])).CreateHotseatMatch(
    ruleset,
    preparedFixedInducements.HomeTeam,
    preparedFixedInducements.AwayTeam,
    preparedFixedInducements.Inducements.Home,
    preparedFixedInducements.Inducements.Away);
Assert(fixedInducementMatch.HomeBloodweiserKegs == 1, "Bloodweiser Kegs should be carried into match state");
Assert(fixedInducementMatch.HomeWeatherMagesRemaining == 1, "Weather Mages should be carried into match state");
Assert(fixedInducementMatch.HomeSpecialPlaysRemaining == 1, "Special Plays should be carried into match state");
Assert(fixedInducementMatch.HomeHasMasterChef, "Master Chef purchases should be carried into match state");
Assert(fixedInducementMatch.HomeRerollsRemaining == loadedLeague.Teams[0].Rerolls + 2, "Master Chef should gain one reroll per 4+ before the half");
Assert(fixedInducementMatch.AwayRerollsRemaining == Math.Max(0, fixedInducementOpponent.Rerolls - 2), "Master Chef should remove the same rerolls from the opponent");
var knockedOutHomePlayer = fixedInducementMatch.Placements.First(placement => placement.TeamId == fixedInducementMatch.HomeTeamId);
var kegRecoveryMatch = fixedInducementMatch with
{
    Half = 1,
    Phase = MatchPhase.DefensiveTurn,
    ActiveTeamId = fixedInducementMatch.AwayTeamId,
    HomeTurn = ruleset.TurnsPerHalf + 1,
    AwayTurn = ruleset.TurnsPerHalf,
    Placements = fixedInducementMatch.Placements
        .Select(placement => placement.PlayerId == knockedOutHomePlayer.PlayerId
            ? placement with { State = PlayerPitchState.KnockedOut, Square = null }
            : placement)
        .ToArray()
};
var kegRecoveryResult = new MatchService(new FixedDiceRoller(d6: [3, 1, 1, 1])).AdvanceTurn(kegRecoveryMatch, ruleset);
Assert(kegRecoveryResult.Placements.Single(placement => placement.PlayerId == knockedOutHomePlayer.PlayerId).State == PlayerPitchState.Reserve, "one Bloodweiser Keg should improve knockout recovery from 4+ to 3+");

var inducementTurn = fixedInducementMatch with
{
    Phase = MatchPhase.OffensivePlayerTurn,
    ActiveTeamId = fixedInducementMatch.HomeTeamId,
    Activations = []
};
var weatherMageResult = new MatchService(new FixedDiceRoller(d6: [6, 6])).UseWeatherMage(inducementTurn, preparedFixedInducements.HomeTeam);
Assert(weatherMageResult.Weather == WeatherCondition.Blizzard && weatherMageResult.HomeWeatherMagesRemaining == 0, "Weather Mage should roll new weather and be consumed");
var specialPlayResult = new MatchService(new FixedDiceRoller(d6: [3])).UseSpecialPlay(inducementTurn, preparedFixedInducements.HomeTeam);
Assert(specialPlayResult.HomeBribesRemaining == inducementTurn.HomeBribesRemaining + 1 && specialPlayResult.HomeSpecialPlaysRemaining == 0, "Special Play table result 3 should grant a bribe and consume the play");

// BB2020 Prayers to Nuffle: the 200k-poorer home team is the underdog and rolls 200,000 / 50,000 = 4 prayers.
// D16 rolls 5/8/4/16 select Knuckle Dusters, Blessed Statue, Iron Man, and Intensive Training (all "choose"
// prayers that deterministically pick the lowest-numbered eligible non-Loner player).
var prayerService = new PreGameService(new FixedDiceRoller(d16: [5, 8, 4, 16]));
var prayerPrepared = prayerService.PrepareMatch(ruleset, rosterSet, loadedLeague.Teams[0], richerAwayTeam);
Assert(prayerPrepared.Prayers.Count == 4, "underdog should roll one Prayer to Nuffle per full 50,000 gp of team value difference");
Assert(prayerPrepared.Prayers.All(prayer => prayer.TeamId == loadedLeague.Teams[0].Id), "prayers should belong to the underdog team");
Assert(prayerPrepared.Prayers.Any(prayer => prayer.Prayer == PrayerToNuffle.KnuckleDusters && prayer.EffectApplied), "Knuckle Dusters should be applied to the match-only team");
var underdogFirstPlayer = loadedLeague.Teams[0].Players.OrderBy(player => player.Number).First();
var preparedFirstPlayer = prayerPrepared.HomeTeam.Players.Single(player => player.Id == underdogFirstPlayer.Id);
Assert(preparedFirstPlayer.Skills.Contains("mighty-blow", StringComparer.OrdinalIgnoreCase), "Knuckle Dusters should grant Mighty Blow to the chosen player");
Assert(preparedFirstPlayer.Stats.Armor == underdogFirstPlayer.Stats.Armor + 1, "Iron Man should raise the chosen player's Armour Value by 1");
var prayerMatch = new MatchService().CreateHotseatMatch(ruleset, prayerPrepared.HomeTeam, prayerPrepared.AwayTeam, prayerPrepared.Inducements.Home, prayerPrepared.Inducements.Away, prayerPrepared.Prayers);
Assert(prayerMatch.Prayers.Count == 4, "match state should record the rolled prayers");
Assert(prayerMatch.Log.Any(entry => entry.Message.Contains("Prayer to Nuffle", StringComparison.Ordinal)), "match log should note the prayers to Nuffle");

var snotlingTeam = loadedLeague.Teams[0] with { RosterId = "snotling" };
var riotousPlan = new PreGameService().CreatePlan(
    ruleset,
    rosterSet,
    snotlingTeam,
    richerAwayTeam,
    homeBribes: 0,
    awayBribes: 0,
    homeInducements: [new SelectedInducement { InducementId = "riotous-rookies", Count = 1 }]);
var riotousPrepared = new PreGameService(new FixedDiceRoller(d6: [1, 1])).PrepareMatch(ruleset, rosterSet, snotlingTeam, richerAwayTeam, riotousPlan);
Assert(riotousPrepared.HomeTeam.Players.Count == snotlingTeam.Players.Count + 3, "Riotous Rookies should add 2D3+1 temporary linemen");
Assert(riotousPrepared.HomeTeam.Players.Count(player => player.Name.StartsWith("Riotous Rookie", StringComparison.Ordinal)) == 3, "Riotous Rookies should be identifiable match-only players");
AssertThrows(
    () => preGameService.PrepareMatch(
        ruleset,
        rosterSet,
        loadedLeague.Teams[0],
        richerAwayTeam,
        preGameService.CreatePlan(
            ruleset,
            rosterSet,
            loadedLeague.Teams[0],
            richerAwayTeam,
            homeBribes: 0,
            awayBribes: 0,
            homeInducements: [new SelectedInducement { InducementId = "wizard", Count = 1 }])),
    "picker-backed inducements should require an option selection");
Assert(preGameSummary.Home.AvailableInducements.Single(inducement => inducement.Id == "mercenary-player").PickerOptions.Any(option => option.Id == "lineman" && option.Cost == 80_000), "mercenary picker options should be generated from roster positions with the mercenary premium");
Assert(preGameSummary.Home.AvailableInducements.Single(inducement => inducement.Id == "wizard").PickerOptions.Count == 2, "wizard picker should expose both configured spell options");

var mixedPickerPlan = preGameService.CreatePlan(
    ruleset,
    rosterSet,
    loadedLeague.Teams[0],
    fixedInducementOpponent,
    homeBribes: 0,
    awayBribes: 0,
    homeInducements:
    [
        new SelectedInducement { InducementId = "mercenary-player", OptionId = "lineman", Count = 1 },
        new SelectedInducement { InducementId = "mercenary-player", OptionId = "thrower", Count = 1 },
        new SelectedInducement { InducementId = "infamous-coaching-staff", OptionId = "legendary-tactics-coach", Count = 1 },
        new SelectedInducement { InducementId = "infamous-coaching-staff", OptionId = "legendary-physio", Count = 1 }
    ]);
var preparedMixedPickers = preGameService.PrepareMatch(ruleset, rosterSet, loadedLeague.Teams[0], fixedInducementOpponent, mixedPickerPlan);
var mixedMercenaries = preparedMixedPickers.HomeTeam.Players.Where(player => player.Injuries.Contains("mercenary")).ToArray();
Assert(mixedMercenaries.Length == 2 && mixedMercenaries.Any(player => player.PositionId == "lineman") && mixedMercenaries.Any(player => player.PositionId == "thrower"), "picker families should support mixed option selections in one plan");
Assert(preparedMixedPickers.HomeTeam.Rerolls == loadedLeague.Teams[0].Rerolls + 1 && preparedMixedPickers.HomeTeam.AssistantCoaches == loadedLeague.Teams[0].AssistantCoaches + 1, "Legendary Tactics Coach should add a reroll and assistant coach");
Assert(preparedMixedPickers.HomeTeam.Apothecaries == loadedLeague.Teams[0].Apothecaries + 1, "mixed staff options should also apply the Legendary Physio effect");
AssertThrows(
    () => preGameService.PrepareMatch(
        ruleset,
        rosterSet,
        loadedLeague.Teams[0],
        fixedInducementOpponent,
        mixedPickerPlan with
        {
            Home = mixedPickerPlan.Home with
            {
                Inducements =
                [
                    new SelectedInducement { InducementId = "infamous-coaching-staff", OptionId = "legendary-tactics-coach", Count = 2 },
                    new SelectedInducement { InducementId = "infamous-coaching-staff", OptionId = "legendary-physio", Count = 1 }
                ]
            }
        }),
    "picker family maximums should apply across different options");

var pickerPlan = preGameService.CreatePlan(
    ruleset,
    rosterSet,
    loadedLeague.Teams[0],
    fixedInducementOpponent,
    homeBribes: 0,
    awayBribes: 0,
    homeInducements:
    [
        new SelectedInducement { InducementId = "mercenary-player", OptionId = "lineman", Count = 1 },
        new SelectedInducement { InducementId = "infamous-coaching-staff", OptionId = "legendary-physio", Count = 1 },
        new SelectedInducement { InducementId = "wizard", OptionId = "lightning-wizard", Count = 1 },
        new SelectedInducement { InducementId = "biased-referee", OptionId = "friendly-referee", Count = 1 }
    ]);
var preparedPickerMatch = preGameService.PrepareMatch(ruleset, rosterSet, loadedLeague.Teams[0], fixedInducementOpponent, pickerPlan);
var mercenary = preparedPickerMatch.HomeTeam.Players.Single(player => player.Injuries.Contains("mercenary"));
Assert(mercenary.PositionId == "lineman" && mercenary.Skills.Contains("loner"), "selected mercenary position should create a temporary Loner player");
Assert(preparedPickerMatch.HomeTeam.Apothecaries == loadedLeague.Teams[0].Apothecaries + 1, "Legendary Physio should add a temporary apothecary");

var pickerMatchService = new MatchService(new FixedDiceRoller(d6: [2, 6, 6, 6, 6], d16: [1]));
pickerMatchService.RegisterTeams(preparedPickerMatch.HomeTeam, preparedPickerMatch.AwayTeam);
var pickerMatch = pickerMatchService.CreateHotseatMatch(ruleset, preparedPickerMatch.HomeTeam, preparedPickerMatch.AwayTeam, pickerPlan.Home, pickerPlan.Away);
Assert(pickerMatch.HomeWizardEffect == "wizard-lightning" && pickerMatch.HomeWizardsRemaining == 1, "selected wizard option should be carried into match state");
Assert(pickerMatch.HomeBloodweiserKegs == 1, "Legendary Physio should add a knockout recovery bonus");
Assert(pickerMatch.HomeBribesRemaining == 1 && pickerMatch.HomeBribeRollModifier == 1, "Friendly Referee should add a bribe and improve bribe rolls");

var lightningVictim = preparedPickerMatch.AwayTeam.Players[0];
var lightningSquare = new PitchSquare(15, 5);
var wizardReadyMatch = pickerMatch with
{
    Phase = MatchPhase.OffensivePlayerTurn,
    ActiveTeamId = pickerMatch.HomeTeamId,
    Placements = pickerMatch.Placements
        .Select(placement => placement.PlayerId == lightningVictim.Id
            ? placement with { State = PlayerPitchState.Standing, Square = lightningSquare }
            : placement)
        .ToArray()
};
var lightningResult = pickerMatchService.UseWizard(wizardReadyMatch, ruleset, preparedPickerMatch.HomeTeam, lightningSquare);
Assert(lightningResult.HomeWizardsRemaining == 0, "using a selected wizard should consume its spell");
Assert(lightningResult.Placements.Single(placement => placement.PlayerId == lightningVictim.Id).State != PlayerPitchState.Standing, "Lightning should knock down its target on 2+");

var refereePlayer = preparedPickerMatch.HomeTeam.Players[0];
var refereeReadyMatch = pickerMatch with
{
    Phase = MatchPhase.OffensivePlayerTurn,
    ActiveTeamId = pickerMatch.HomeTeamId,
    PendingSendOff = new PendingSendOffChoice
    {
        TeamId = pickerMatch.HomeTeamId,
        PlayerId = refereePlayer.Id,
        Reason = "test foul",
        BribeAvailable = true
    }
};
var refereeResultService = new MatchService(new FixedDiceRoller(d6: [1]));
var refereeResult = refereeResultService.ResolvePendingSendOff(refereeReadyMatch, ruleset, preparedPickerMatch.HomeTeam, useBribe: true);
Assert(refereeResult.Placements.Single(placement => placement.PlayerId == refereePlayer.Id).State != PlayerPitchState.SentOff, "Friendly Referee +1 should turn a bribe roll of 1 into a success");

var fireballPlan = preGameService.CreatePlan(
    ruleset,
    rosterSet,
    loadedLeague.Teams[0],
    fixedInducementOpponent,
    homeBribes: 0,
    awayBribes: 0,
    homeInducements: [new SelectedInducement { InducementId = "wizard", OptionId = "fireball-wizard", Count = 1 }]);
var preparedFireball = preGameService.PrepareMatch(ruleset, rosterSet, loadedLeague.Teams[0], fixedInducementOpponent, fireballPlan);
var fireballService = new MatchService(new FixedDiceRoller(d6: [6, 6, 6, 6, 6, 6, 6, 6, 6, 6], d16: [1, 1]));
fireballService.RegisterTeams(preparedFireball.HomeTeam, preparedFireball.AwayTeam);
var fireballMatch = fireballService.CreateHotseatMatch(ruleset, preparedFireball.HomeTeam, preparedFireball.AwayTeam, fireballPlan.Home, fireballPlan.Away);
var fireballHomeVictim = preparedFireball.HomeTeam.Players[0];
var fireballAwayVictim = preparedFireball.AwayTeam.Players[0];
var fireballCenter = new PitchSquare(13, 7);
var fireballReadyMatch = fireballMatch with
{
    Phase = MatchPhase.OffensivePlayerTurn,
    ActiveTeamId = fireballMatch.HomeTeamId,
    Placements = fireballMatch.Placements.Select(placement =>
        placement.PlayerId == fireballHomeVictim.Id
            ? placement with { State = PlayerPitchState.Standing, Square = fireballCenter }
            : placement.PlayerId == fireballAwayVictim.Id
                ? placement with { State = PlayerPitchState.Standing, Square = new PitchSquare(14, 7) }
                : placement with { Square = null }).ToArray()
};
var fireballResult = fireballService.UseWizard(fireballReadyMatch, ruleset, preparedFireball.HomeTeam, fireballCenter);
Assert(fireballMatch.HomeWizardEffect == "wizard-fireball" && fireballResult.HomeWizardsRemaining == 0, "Fireball selection should be carried into match state and consumed on use");
Assert(fireballResult.Placements.Where(placement => placement.PlayerId == fireballHomeVictim.Id || placement.PlayerId == fireballAwayVictim.Id).All(placement => placement.State != PlayerPitchState.Standing), "Fireball should affect standing players from both teams in its 3x3 area on 4+");

var intimidatingPlan = preGameService.CreatePlan(
    ruleset,
    rosterSet,
    loadedLeague.Teams[0],
    fixedInducementOpponent,
    homeBribes: 0,
    awayBribes: 0,
    homeInducements: [new SelectedInducement { InducementId = "biased-referee", OptionId = "intimidating-referee", Count = 1 }]);
var preparedIntimidating = preGameService.PrepareMatch(ruleset, rosterSet, loadedLeague.Teams[0], fixedInducementOpponent, intimidatingPlan);
var intimidatingMatch = new MatchService(new FixedDiceRoller(d6: [3, 3])).CreateHotseatMatch(ruleset, preparedIntimidating.HomeTeam, preparedIntimidating.AwayTeam, intimidatingPlan.Home, intimidatingPlan.Away);
Assert(intimidatingMatch.AwayBribeRollModifier == -1, "Intimidating Referee should penalize the opposing team's bribe rolls");
var intimidatedPlayer = preparedIntimidating.AwayTeam.Players[0];
var intimidatedSendOff = intimidatingMatch with
{
    Phase = MatchPhase.OffensivePlayerTurn,
    ActiveTeamId = intimidatingMatch.AwayTeamId,
    AwayBribesRemaining = 1,
    PendingSendOff = new PendingSendOffChoice
    {
        TeamId = intimidatingMatch.AwayTeamId,
        PlayerId = intimidatedPlayer.Id,
        Reason = "test foul",
        BribeAvailable = true
    }
};
var intimidatedResult = new MatchService(new FixedDiceRoller(d6: [2])).ResolvePendingSendOff(intimidatedSendOff, ruleset, preparedIntimidating.AwayTeam, useBribe: true);
Assert(intimidatedResult.Placements.Single(placement => placement.PlayerId == intimidatedPlayer.Id).State == PlayerPitchState.SentOff, "Intimidating Referee -1 should turn a bribe roll of 2 into a failure");
Assert(preGameSummary.StarPlayersSupported, "pre-game should report star player support when roster data defines stars");
Assert(preGameSummary.Home.EligibleStarPlayers.Any(star => star.Id == "griff-oberwald"), "pre-game summary should list star players eligible for the team's roster special rules");
Assert(preGameSummary.Home.EligibleStarPlayers.All(star => star.MatchedSpecialRules.Count > 0), "eligible star player summaries should name the matched roster special rules");
var restrictedRosterTeam = loadedLeague.Teams[0] with { RosterId = "old-world-alliance" };
var restrictedRosterSummary = preGameService.BuildSummary(ruleset, rosterSet, restrictedRosterTeam, richerAwayTeam);
Assert(restrictedRosterSummary.Home.RosterRestrictions.Contains("mixed-position-animosity"), "pre-game summary should surface roster restriction metadata");
var createdStarPlan = preGameService.CreatePlan(
    ruleset,
    rosterSet,
    loadedLeague.Teams[0],
    richerAwayTeam,
    homeBribes: 0,
    awayBribes: 0,
    homeTreasurySpent: 80_000,
    homeStarPlayerIds: ["griff-oberwald"]);
Assert(createdStarPlan.Home.StarPlayerIds.SequenceEqual(["griff-oberwald"]), "pre-game CreatePlan should carry selected star players for the UI");
var starPlan = preGameService.CreateDefaultPlan(ruleset, rosterSet, loadedLeague.Teams[0], richerAwayTeam) with
{
    Home = preGameService.CreateDefaultPlan(ruleset, rosterSet, loadedLeague.Teams[0], richerAwayTeam).Home with
    {
        TreasurySpent = 80_000,
        StarPlayerIds = ["griff-oberwald"]
    }
};
var preparedStarMatch = preGameService.PrepareMatch(ruleset, rosterSet, loadedLeague.Teams[0], richerAwayTeam, starPlan);
Assert(preparedStarMatch.HomeTeam.Players.Any(player => player.Injuries.Contains("star-player") && player.Name == "Griff Oberwald"), "pre-game should add eligible star players to the match team");
AssertThrows(
    () => preGameService.CreatePlan(ruleset, rosterSet, loadedLeague.Teams[0], richerAwayTeam, homeBribes: 3, awayBribes: 0),
    "pre-game should reject bribes that exceed petty cash and selected treasury spend");
AssertThrows(
    () => preGameService.PrepareMatch(ruleset, rosterSet, loadedLeague.Teams[0], richerAwayTeam, starPlan with { Home = starPlan.Home with { StarPlayerIds = ["varag-ghoul-chewer"] } }),
    "pre-game should reject star players that are not eligible for a team's special rules");
var tooManyStarsPlan = preGameService.CreatePlan(
    ruleset,
    rosterSet,
    loadedLeague.Teams[0],
    fixedInducementOpponent,
    homeBribes: 0,
    awayBribes: 0,
    homeStarPlayerIds: ["griff-oberwald", "morg-n-thorg", "deeproot-strongbranch"]);
AssertThrows(
    () => preGameService.PrepareMatch(ruleset, rosterSet, loadedLeague.Teams[0], fixedInducementOpponent, tooManyStarsPlan),
    "pre-game should enforce the ruleset maximum of two Star Players per team");
AssertThrows(
    () => preGameService.PrepareMatch(
        ruleset,
        rosterSet,
        loadedLeague.Teams[0],
        richerAwayTeam,
        starPlan with
        {
            Home = starPlan.Home with { StarPlayerIds = ["morg-n-thorg"] },
            Away = starPlan.Away with { StarPlayerIds = ["morg-n-thorg"] }
        }),
    "pre-game should reject the same star player selected for both teams");
Assert(loadedMatch.HomeTeamId == loadedLeague.Teams[0].Id, "match home team should round-trip");
Assert(loadedMatch.AwayTeamId == awayLeague.Teams[0].Id, "match away team should round-trip");
Assert(loadedMatch.Phase == MatchPhase.DefenseSetup, "match should start with defense setup");
Assert(loadedMatch.ActiveTeamId == awayLeague.Teams[0].Id, "away team should set up defense first");
Assert(loadedMatch.HomeTurn == 1 && loadedMatch.AwayTurn == 1, "both teams should start half one on turn one");
Assert(loadedMatch.FirstHalfReceivingTeamId == loadedLeague.Teams[0].Id, "home team should be recorded as the first-half receiving team");
Assert(loadedMatch.Placements.Count == 22, "match should place both teams in reserve");

var awayPlayerToPlace = awayLeague.Teams[0].Players[0];
var incompleteDefenseSetupMatch = matchService.PlacePlayer(loadedMatch, ruleset, awayPlayerToPlace.Id, new(20, 5));
AssertThrows(
    () => matchService.AdvancePhase(incompleteDefenseSetupMatch, ruleset),
    "defense setup should require a complete legal formation before advancing");

var defenseSetupMatch = SetupTeam(matchService, loadedMatch, ruleset, awayLeague.Teams[0], [
    new(20, 5),
    new(13, 4),
    new(13, 5),
    new(13, 6),
    new(20, 4),
    new(20, 6),
    new(20, 7),
    new(20, 8),
    new(20, 9),
    new(20, 10),
    new(20, 11)
]);
var defensePlacedPlayer = defenseSetupMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);

Assert(defensePlacedPlayer.State == PlayerPitchState.Standing, "defense player should stand on the pitch");
Assert(defensePlacedPlayer.Square == new PitchSquare(20, 5), "defense player should keep assigned square");

var knockedOutSetupMatch = loadedMatch with
{
    Placements = loadedMatch.Placements
        .Select(placement => placement.PlayerId == awayPlayerToPlace.Id
            ? placement with { State = PlayerPitchState.KnockedOut }
            : placement)
        .ToArray()
};
AssertThrows(
    () => matchService.PlacePlayer(knockedOutSetupMatch, ruleset, awayPlayerToPlace.Id, new(20, 5)),
    "knocked out players should not be placeable during kickoff setup");

var offenseSetupMatch = matchService.AdvancePhase(defenseSetupMatch, ruleset);
Assert(offenseSetupMatch.Phase == MatchPhase.OffenseSetup, "defense setup should advance to offense setup");
Assert(offenseSetupMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "home team should set up offense");

var playerToPlace = loadedLeague.Teams[0].Players[0];
var noLineSetupMatch = SetupTeam(matchService, offenseSetupMatch, ruleset, loadedLeague.Teams[0], [
    new(0, 0),
    new(1, 4),
    new(1, 5),
    new(1, 6),
    new(1, 7),
    new(1, 8),
    new(1, 9),
    new(1, 10),
    new(1, 11),
    new(2, 4),
    new(2, 5)
]);
AssertThrows(
    () => matchService.AdvancePhase(noLineSetupMatch, ruleset),
    "offense setup should require three players on the line of scrimmage");

var placedMatch = SetupTeam(matchService, offenseSetupMatch, ruleset, loadedLeague.Teams[0], [
    new(0, 0),
    new(12, 4),
    new(12, 5),
    new(12, 6),
    new(1, 4),
    new(1, 5),
    new(1, 6),
    new(1, 7),
    new(1, 8),
    new(1, 9),
    new(1, 10)
]);
var placedPlayer = placedMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(placedPlayer.State == PlayerPitchState.Standing, "offense player should stand on the pitch");
Assert(placedPlayer.Square == new PitchSquare(0, 0), "offense player should keep assigned square");

smoke.StartSection("Kickoff, weather, and kickoff events");
var kickoffMatch = matchService.AdvancePhase(placedMatch, ruleset);
// This match was created with a non-deterministic dice roller, so its per-game Fan Factor/FAME are random.
// Normalize them here so the fixed-dice kickoff-event assertions below remain deterministic; the focused
// FAME contest tests override these explicitly.
kickoffMatch = kickoffMatch with { HomeFanFactor = 1, AwayFanFactor = 1, HomeFame = 0, AwayFame = 0 };
Assert(kickoffMatch.Phase == MatchPhase.Kickoff, "offense setup should advance to kickoff");
Assert(matchService.AdvancePhase(kickoffMatch, ruleset).Phase == MatchPhase.Kickoff, "generic phase advance should not skip unresolved kickoff");

var kickoffService = new MatchService(new FixedDiceRoller(d6: [3, 3, 3, 3, 1], d8: [5, 5]));
var offensiveTurnMatch = kickoffService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(2, 2));
Assert(offensiveTurnMatch.Phase == MatchPhase.OffensivePlayerTurn, "kickoff should advance to offensive player turn");
Assert(offensiveTurnMatch.Drive == 1 && offensiveTurnMatch.DriveState == DriveState.InProgress, "resolved kickoff should mark the drive in progress");
Assert(offensiveTurnMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "home team should have the offensive turn");
Assert(offensiveTurnMatch.Ball.Square == new PitchSquare(4, 2), "kickoff landing on an empty square should bounce once before resting");

var longKickoffScatterService = new MatchService(new FixedDiceRoller(d6: [3, 3, 3, 3, 3], d8: [5, 5]));
var longKickoffScatterMatch = longKickoffScatterService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(2, 2));

Assert(longKickoffScatterMatch.Ball.Square == new PitchSquare(6, 2), "kickoff scatter should move d6 squares in the d8 direction, then bounce once on the empty landing");

// BB2020 On the Ball: after the kick but before the kick-off event roll, the receiving team may move a
// player with the skill up to three squares (staying in their own half).
var kickoffOnTheBallTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "on-the-ball"] } : player)
        .ToArray()
};
var kickoffOnTheBallMatch = kickoffMatch with
{
    Placements = kickoffMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(3, 3), State = PlayerPitchState.Standing }
            : placement with { Square = null, State = PlayerPitchState.Reserve })
        .ToArray()
};
var kickoffOnTheBallService = new MatchService(new FixedDiceRoller(d6: [3, 3, 3, 3, 1], d8: [5, 5]));
var kickoffOnTheBallPending = kickoffOnTheBallService.ResolveKickoff(kickoffOnTheBallMatch, ruleset, kickoffOnTheBallTeam, new(2, 2));
Assert(kickoffOnTheBallPending.PendingOnTheBall?.Trigger == OnTheBallTrigger.Kickoff && kickoffOnTheBallPending.PendingOnTheBall.EligiblePlayerIds.Contains(playerToPlace.Id), "kickoff should offer On the Ball before the event roll");
Assert(kickoffOnTheBallPending.Phase == MatchPhase.Kickoff, "an unresolved kickoff On the Ball should keep the kickoff phase");

var declinedKickoffOnTheBall = kickoffOnTheBallService.ResolvePendingOnTheBall(kickoffOnTheBallPending, ruleset, kickoffOnTheBallTeam, awayLeague.Teams[0], null, null);
Assert(declinedKickoffOnTheBall.PendingOnTheBall is null && declinedKickoffOnTheBall.Phase == MatchPhase.OffensivePlayerTurn, "declining kickoff On the Ball should resume and resolve the kickoff");

var kickoffOnTheBallMoveService = new MatchService(new FixedDiceRoller(d6: [3, 3, 3, 3, 1], d8: [5, 5]));
var kickoffOnTheBallMovePending = kickoffOnTheBallMoveService.ResolveKickoff(kickoffOnTheBallMatch, ruleset, kickoffOnTheBallTeam, new(2, 2));
var movedKickoffOnTheBall = kickoffOnTheBallMoveService.ResolvePendingOnTheBall(kickoffOnTheBallMovePending, ruleset, kickoffOnTheBallTeam, awayLeague.Teams[0], playerToPlace.Id, new PitchSquare(6, 3));
Assert(movedKickoffOnTheBall.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(6, 3), "kickoff On the Ball should move the receiver up to three squares");
Assert(movedKickoffOnTheBall.PendingOnTheBall is null && movedKickoffOnTheBall.Phase == MatchPhase.OffensivePlayerTurn, "moving a kickoff On the Ball player should resume and resolve the kickoff");

var caughtKickoffService = new MatchService(new FixedDiceRoller(d6: [3, 3, 3, 3, 1, 4], d8: [1]));
var caughtKickoffMatch = caughtKickoffService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(1, 1));

Assert(caughtKickoffMatch.Ball.CarrierPlayerId == playerToPlace.Id, "kickoff landing on receiver should allow a catch");

// A dropped kickoff catch lets the receiving team spend a team reroll (the kickoff transition is applied
// first so the catch is made during their turn). Strip skills so the failure is not auto-rerolled.
var kickoffCatchTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [] } : player)
        .ToArray()
};
var kickoffRerollService = new MatchService(new FixedDiceRoller(d6: [3, 3, 3, 3, 1, 1, 6], d8: [1]));
var kickoffRerollPending = kickoffRerollService.ResolveKickoff(kickoffMatch, ruleset, kickoffCatchTeam, new(1, 1));
Assert(kickoffRerollPending.PendingReroll?.Kind == PendingRerollKind.Catch, "a dropped kickoff catch should offer the receiving team a reroll");
Assert(kickoffRerollPending.Phase == MatchPhase.OffensivePlayerTurn, "the kickoff transition should be applied while the catch reroll is pending");
var kickoffRerollResolved = kickoffRerollService.ResolvePendingReroll(kickoffRerollPending, ruleset, kickoffCatchTeam, useTeamReroll: true);
Assert(kickoffRerollResolved.Ball.CarrierPlayerId == playerToPlace.Id, "a successful team reroll should let the receiver catch the kickoff");

var touchbackService = new MatchService(new FixedDiceRoller(d6: [3, 3, 3, 3, 1], d8: [5]));
var touchbackMatch = touchbackService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(ruleset.PitchWidth / 2, 0));
Assert(touchbackMatch.PendingBallPlacement?.Reason == "Touchback", "kickoff outside receiving half should let the receiving team choose a touchback carrier");
var resolvedTouchbackMatch = touchbackService.ChooseBallPlacement(touchbackMatch, loadedLeague.Teams[0], placedPlayer.Square!);

Assert(resolvedTouchbackMatch.Ball.CarrierPlayerId == playerToPlace.Id, "chosen touchback player should receive the ball");

var getRefService = new MatchService(new FixedDiceRoller(d6: [1, 1, 1], d8: [5]));
var getRefMatch = getRefService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(2, 2));
Assert(getRefMatch.HomeBribesRemaining == 1 && getRefMatch.AwayBribesRemaining == 1, "get the ref should award both teams a bribe");

var cheeringFansService = new MatchService(new FixedDiceRoller(d6: [3, 3, 1, 6, 1], d8: [5]));
var cheeringFansMatch = cheeringFansService.ResolveKickoff(
    kickoffMatch with { HomeCheerleaders = 3, HomeFanFactor = 1, AwayFanFactor = 1, HomeFame = 0, AwayFame = 0 },
    ruleset,
    loadedLeague.Teams[0],
    new(2, 2));
Assert(cheeringFansMatch.HomeRerollsRemaining == loadedLeague.Teams[0].Rerolls + 1, "cheerleaders should modify Cheering Fans kickoff contests");

var fameKickoffService = new MatchService(new FixedDiceRoller(d6: [3, 3, 1, 1, 1], d8: [5]));
var fameKickoffMatch = fameKickoffService.ResolveKickoff(
    kickoffMatch with { HomeFanFactor = 6, AwayFanFactor = 1, HomeFame = 2, AwayFame = 0 },
    ruleset,
    loadedLeague.Teams[0],
    new(2, 2));
Assert(fameKickoffMatch.HomeRerollsRemaining == loadedLeague.Teams[0].Rerolls + 1, "FAME should modify Cheering Fans kickoff contests (BB2020)");

var changingWeatherKickoffService = new MatchService(new FixedDiceRoller(d6: [4, 4, 3, 3, 1], d8: [5, 5, 5]));
var changingWeatherKickoffMatch = changingWeatherKickoffService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(2, 2));

Assert(changingWeatherKickoffMatch.Weather == WeatherCondition.Nice, "changing weather kickoff event should update match weather");
Assert(changingWeatherKickoffMatch.Ball.Square == new PitchSquare(5, 2), "nice weather changing-weather event should add an extra gust scatter, then bounce once on the empty landing");
Assert(changingWeatherKickoffMatch.Log.Any(entry => entry.Message.Contains("Kickoff event roll 8", StringComparison.Ordinal)), "kickoff should log the table result");

var highKickService = new MatchService(new FixedDiceRoller(d6: [2, 3, 1], d8: [5]));
var highKickMatch = highKickService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(2, 2));
Assert(highKickMatch.PendingKickoffEvent?.Kind == KickoffEventKind.HighKick, "high kick should create a pending receiver choice");
var highKickLanding = highKickMatch.PendingKickoffEvent!.LandingSquare;
var highKickMoved = highKickService.MovePendingKickoffEventPlayer(highKickMatch, ruleset, playerToPlace.Id, highKickLanding);
var highKickResolved = highKickService.CompletePendingKickoffEvent(highKickMoved, ruleset, loadedLeague.Teams[0]);
Assert(highKickResolved.Phase == MatchPhase.OffensivePlayerTurn, "high kick should resolve to the receiving player turn after the choice");
Assert(highKickResolved.Ball.CarrierPlayerId == playerToPlace.Id, "high kick receiver under the ball should get the catch attempt");
Assert(highKickResolved.DriveState == DriveState.InProgress, "resolving a kickoff event should mark the drive in progress");

var quickSnapService = new MatchService(new FixedDiceRoller(d6: [4, 5, 1, 1], d8: [5]));
var quickSnapMatch = quickSnapService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(2, 2));
Assert(quickSnapMatch.PendingKickoffEvent?.Kind == KickoffEventKind.QuickSnap, "quick snap should create a pending free-move choice");
Assert(quickSnapMatch.PendingKickoffEvent?.MovesRemaining == quickSnapMatch.PendingKickoffEvent?.EligiblePlayerIds.Count, "BB2020 quick snap should let every open player move, not just D3+3");
var quickSnapMoved = quickSnapService.MovePendingKickoffEventPlayer(quickSnapMatch, ruleset, playerToPlace.Id, new(1, 0));
Assert(quickSnapMoved.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 0), "quick snap should move an open receiving player one square");

var solidDefenceService = new MatchService(new FixedDiceRoller(d6: [2, 2, 1, 5], d8: [5]));
var solidDefenceMatch = solidDefenceService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(2, 2));
Assert(solidDefenceMatch.PendingKickoffEvent?.Kind == KickoffEventKind.SolidDefence, "solid defence should create a pending defensive reposition choice");
Assert(solidDefenceMatch.PendingKickoffEvent?.MovesRemaining == 6, "solid defence should allow D3+3 defensive players to be repositioned");
AssertThrows(
    () => solidDefenceService.MovePendingKickoffEventPlayer(solidDefenceMatch, ruleset, awayPlayerToPlace.Id, new(1, 1)),
    "solid defence should reject repositioning into the receiving team's half");
var solidDefenceMoved = solidDefenceService.MovePendingKickoffEventPlayer(solidDefenceMatch, ruleset, awayPlayerToPlace.Id, new(18, 5));
Assert(solidDefenceMoved.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).Square == new PitchSquare(18, 5), "solid defence should allow defensive players to be set up in different legal places");

// Officious Ref (kickoff 11): coaches roll D6 + FAME; the lower coach's randomly chosen player is stunned
// (2+) or sent off (1). Event roll 5+6 = 11; receiving rolls 1 vs kicking 6, so a receiving player is hit
// and the ref roll of 4 leaves them stunned.
var officiousRefService = new MatchService(new FixedDiceRoller(d6: [5, 6, 1, 6, 1, 4, 1], d8: [5]));
var officiousRefMatch = officiousRefService.ResolveKickoff(kickoffMatch, ruleset, loadedLeague.Teams[0], new(2, 2));
Assert(officiousRefMatch.Log.Any(entry => entry.Message.Contains("Officious Ref", StringComparison.Ordinal)), "officious ref should resolve and log the ref singling out a player");
Assert(officiousRefMatch.Placements.Count(placement => placement.TeamId == loadedLeague.Teams[0].Id && placement.State == PlayerPitchState.Stunned) == 1, "officious ref should stun the selected player on a 2+");

smoke.StartSection("Movement, ball pickup, and scoring");
var declaredMoveMatch = matchService.DeclarePlayerAction(offensiveTurnMatch, loadedLeague.Teams[0], playerToPlace.Id, PlayerTurnAction.Move);
Assert(declaredMoveMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).DeclaredOnly, "declared actions should be recorded before resolution");
var resolvedDeclaredMoveMatch = matchService.MovePlayer(declaredMoveMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(3, 0));
Assert(resolvedDeclaredMoveMatch.Activations.Count(activation => activation.PlayerId == playerToPlace.Id) == 1, "resolving a declared action should not duplicate activation records");
Assert(!resolvedDeclaredMoveMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).DeclaredOnly, "resolved declared actions should no longer be marked declaration-only");

var movedMatch = matchService.MovePlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(3, 0));
var movedPlayer = movedMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(movedPlayer.Square == new PitchSquare(3, 0), "moved player should keep destination square");
Assert(movedMatch.Activations.Count == 1, "movement should activate the player");

var pickupService = new MatchService(new FixedDiceRoller(d6: [2]));
var pickupMatch = pickupService.MovePlayer(
    offensiveTurnMatch with { Ball = new BallState { Square = new PitchSquare(2, 0) } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(3, 0));

Assert(pickupMatch.Ball.CarrierPlayerId == playerToPlace.Id, "moving over a loose ball should pick it up on a successful roll");
Assert(pickupMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(3, 0), "successful pickup should allow movement to continue");

var incrementalPickupService = new MatchService(new FixedDiceRoller(d6: [2]));
var pickupStepMatch = incrementalPickupService.MovePlayer(
    offensiveTurnMatch with { Ball = new BallState { Square = new PitchSquare(2, 0) } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(2, 0));
var continuedAfterPickupMatch = incrementalPickupService.MovePlayer(
    pickupStepMatch,
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(3, 0));
Assert(continuedAfterPickupMatch.Ball.CarrierPlayerId == playerToPlace.Id, "successful pickup in an incremental move should keep the ball carried");
Assert(continuedAfterPickupMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(3, 0), "player should be able to keep moving after an incremental pickup");
Assert(continuedAfterPickupMatch.Activations.Count(activation => activation.PlayerId == playerToPlace.Id) == 1, "continuing movement after pickup should not create a second activation");
Assert(continuedAfterPickupMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).MovementSquaresUsed == 3, "continued movement should track total movement spent");

var rainPickupService = new MatchService(new FixedDiceRoller(d6: [2]));
var rainPickupMatch = rainPickupService.MovePlayer(
    offensiveTurnMatch with
    {
        Weather = WeatherCondition.PouringRain,
        Ball = new BallState { Square = new PitchSquare(2, 0) }
    },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(3, 0));

Assert(rainPickupMatch.PendingReroll?.Kind == PendingRerollKind.Pickup, "pouring rain should make a normal 2+ pickup need 3+");
Assert(rainPickupMatch.PendingReroll?.Target == 3, "pouring rain pickup target should be one worse");

var failedPickupService = new MatchService(new FixedDiceRoller(d6: [1], d8: [5]));
var failedPickupMatch = failedPickupService.MovePlayer(
    offensiveTurnMatch with { Ball = new BallState { Square = new PitchSquare(2, 0) } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(3, 0));
Assert(failedPickupMatch.PendingReroll?.Kind == PendingRerollKind.Pickup, "failed pickup should offer a pending reroll before resolving failure");
failedPickupMatch = failedPickupService.ResolvePendingReroll(failedPickupMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false);

Assert(failedPickupMatch.Phase == MatchPhase.DefensiveTurn, "failed pickup should cause a turnover if the moving team does not recover the bounce");
Assert(failedPickupMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(2, 0), "failed pickup should stop movement on the pickup square");
Assert(failedPickupMatch.Ball.Square == new PitchSquare(3, 0), "failed pickup should bounce the ball from the pickup square");

var outOfBoundsPickupService = new MatchService(new FixedDiceRoller(d6: [1, 3, 3, 3], d8: [1]));
var outOfBoundsPickupMatch = outOfBoundsPickupService.MovePlayer(
    offensiveTurnMatch with
    {
        Ball = new BallState { Square = new PitchSquare(0, 0) },
        Placements = offensiveTurnMatch.Placements
            .Select(placement => placement.PlayerId == playerToPlace.Id
                ? placement with { Square = new PitchSquare(1, 0), State = PlayerPitchState.Standing }
                : placement)
            .ToArray()
    },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(0, 0));
outOfBoundsPickupMatch = outOfBoundsPickupService.ResolvePendingReroll(outOfBoundsPickupMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false);

Assert(outOfBoundsPickupMatch.Ball.Square == new PitchSquare(6, 0), "out-of-bounds ball scatter should be thrown back in instead of clamped to the edge");

var noRerollPickupService = new MatchService(new FixedDiceRoller(d6: [1], d8: [5]));
var noRerollPickupMatch = noRerollPickupService.MovePlayer(
    offensiveTurnMatch with { HomeRerollsRemaining = 0, Ball = new BallState { Square = new PitchSquare(2, 0) } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(3, 0));

Assert(noRerollPickupMatch.PendingReroll is null, "failed pickup with no available rerolls should not create a pending reroll");
Assert(noRerollPickupMatch.Phase == MatchPhase.DefensiveTurn, "failed pickup with no available rerolls should resolve immediately");

var pickupRerollService = new MatchService(new FixedDiceRoller(d6: [1, 2]));
var pickupRerollPendingMatch = pickupRerollService.MovePlayer(
    offensiveTurnMatch with { Ball = new BallState { Square = new PitchSquare(2, 0) } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(3, 0));
var pickupRerollMatch = pickupRerollService.ResolvePendingReroll(pickupRerollPendingMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: true);

Assert(pickupRerollMatch.PendingReroll is null, "successful team reroll should clear pending pickup reroll");
Assert(pickupRerollMatch.Ball.CarrierPlayerId == playerToPlace.Id, "successful pickup reroll should recover the ball");
Assert(pickupRerollMatch.HomeRerollsRemaining == loadedLeague.Teams[0].Rerolls - 1, "team reroll should reduce remaining rerolls");

var touchdownReadyMatch = offensiveTurnMatch with
{
    Ball = new BallState { CarrierPlayerId = playerToPlace.Id },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(ruleset.PitchWidth - 2, 0), State = PlayerPitchState.Standing }
            : placement.PlayerId == loadedLeague.Teams[0].Players[1].Id
                ? placement with { Square = null, State = PlayerPitchState.KnockedOut }
                : placement.PlayerId == awayPlayerToPlace.Id
                    ? placement with { Square = null, State = PlayerPitchState.KnockedOut }
                    : placement)
        .ToArray()
};
var touchdownService = new MatchService(new FixedDiceRoller(d6: [4, 3]));
var scoredMatch = touchdownService.MovePlayer(touchdownReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(ruleset.PitchWidth - 1, 0));

Assert(scoredMatch.HomeScore == 1, "home ball carrier should score in away end zone");
Assert(scoredMatch.AwayScore == 0, "away score should not change on home touchdown");
Assert(scoredMatch.Phase == MatchPhase.DefenseSetup, "touchdown should reset to defense placement");
Assert(scoredMatch.Drive == 2 && scoredMatch.DriveState == DriveState.Setup, "touchdown should advance to the next drive setup state");
Assert(scoredMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "scoring team should set up defense for the next drive");
Assert(scoredMatch.Ball.CarrierPlayerId is null && scoredMatch.Ball.Square is null, "touchdown should clear the ball");
Assert(scoredMatch.Placements.Any(placement => placement.TeamId == loadedLeague.Teams[0].Id && placement.State == PlayerPitchState.Reserve), "touchdown should reset available players to reserve");
Assert(scoredMatch.Placements.Single(placement => placement.PlayerId == loadedLeague.Teams[0].Players[1].Id).State == PlayerPitchState.Reserve, "touchdown should recover knocked out players on 4+");
Assert(scoredMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.KnockedOut, "touchdown should leave failed knockout recoveries knocked out");

var pickMeUpTouchdownService = new MatchService(new FixedDiceRoller(d6: [3]));
var pickMeUpScore = pickMeUpTouchdownService.MovePlayer(
    touchdownReadyMatch with
    {
        PickMeUpPlayerIds = [playerToPlace.Id],
        Placements = touchdownReadyMatch.Placements
            .Select(placement => placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = null, State = PlayerPitchState.Reserve }
                : placement)
            .ToArray()
    },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(ruleset.PitchWidth - 1, 0));
Assert(pickMeUpScore.Placements.Single(placement => placement.PlayerId == loadedLeague.Teams[0].Players[1].Id).State == PlayerPitchState.Reserve, "Pick-me-up should improve friendly knockout recovery to 3+");

var secretWeaponTouchdownService = new MatchService(new FixedDiceRoller(d6: [4]));
var secretWeaponPending = secretWeaponTouchdownService.MovePlayer(
    touchdownReadyMatch with
    {
        HomeBribesRemaining = 1,
        SecretWeaponPlayerIds = [playerToPlace.Id],
        Placements = touchdownReadyMatch.Placements
            .Select(placement => placement.PlayerId == loadedLeague.Teams[0].Players[1].Id || placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = null, State = PlayerPitchState.Reserve }
                : placement)
            .ToArray()
    },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(ruleset.PitchWidth - 1, 0));
Assert(secretWeaponPending.PendingSendOff?.Reason == "Secret Weapon", "drive end should create a pending send-off for Secret Weapon players when a bribe is available");
Assert(secretWeaponPending.DriveState == DriveState.Ending, "pending Secret Weapon choices should keep the drive in ending state");
var secretWeaponBribed = secretWeaponTouchdownService.ResolvePendingSendOff(secretWeaponPending, ruleset, loadedLeague.Teams[0], useBribe: true);
Assert(secretWeaponBribed.HomeBribesRemaining == 0, "using a Secret Weapon bribe should spend the bribe");
Assert(secretWeaponBribed.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Reserve, "successful Secret Weapon bribe should keep the player available for the next drive");
Assert(secretWeaponBribed.Phase == MatchPhase.DefenseSetup && secretWeaponBribed.Drive == 2, "resolving Secret Weapon bribes should continue the next drive setup");

var secondSecretWeaponPlayer = loadedLeague.Teams[0].Players[1];
var secretWeaponQueueService = new MatchService(new FixedDiceRoller(d6: [3, 3]));
var secretWeaponQueuePending = secretWeaponQueueService.MovePlayer(
    touchdownReadyMatch with
    {
        HomeBribesRemaining = 2,
        SecretWeaponPlayerIds = [playerToPlace.Id, secondSecretWeaponPlayer.Id],
        Placements = touchdownReadyMatch.Placements
            .Select(placement => placement.PlayerId == secondSecretWeaponPlayer.Id
                ? placement with { Square = new PitchSquare(0, 1), State = PlayerPitchState.Standing }
                : placement.PlayerId == awayPlayerToPlace.Id
                    ? placement with { Square = null, State = PlayerPitchState.Reserve }
                    : placement)
            .ToArray()
    },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(ruleset.PitchWidth - 1, 0));
Assert(secretWeaponQueuePending.PendingSendOff?.PlayerId == playerToPlace.Id, "drive end should queue the first Secret Weapon send-off choice");
Assert(secretWeaponQueuePending.Log.Last().Message.Contains("1 more Secret Weapon", StringComparison.Ordinal), "drive end send-off presentation should report the remaining Secret Weapon queue");
var secondSecretWeaponPending = secretWeaponQueueService.ResolvePendingSendOff(secretWeaponQueuePending, ruleset, loadedLeague.Teams[0], useBribe: true);
Assert(secondSecretWeaponPending.PendingSendOff?.PlayerId == secondSecretWeaponPlayer.Id, "resolving the first Secret Weapon should continue to the next queued send-off");
var completedSecretWeaponQueue = secretWeaponQueueService.ResolvePendingSendOff(secondSecretWeaponPending, ruleset, loadedLeague.Teams[0], useBribe: false);
Assert(completedSecretWeaponQueue.Phase == MatchPhase.DefenseSetup && completedSecretWeaponQueue.Drive == 2, "resolving the Secret Weapon queue should continue into next drive setup");

var secretWeaponNoBribeService = new MatchService(new FixedDiceRoller());
var secretWeaponSentOff = secretWeaponNoBribeService.MovePlayer(
    touchdownReadyMatch with
    {
        SecretWeaponPlayerIds = [playerToPlace.Id],
        Placements = touchdownReadyMatch.Placements
            .Select(placement => placement.PlayerId == loadedLeague.Teams[0].Players[1].Id || placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = null, State = PlayerPitchState.Reserve }
                : placement)
            .ToArray()
    },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(ruleset.PitchWidth - 1, 0));
Assert(secretWeaponSentOff.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.SentOff, "Secret Weapon players without bribes should be sent off after a drive");

smoke.StartSection("Hand-offs, passing, and interference");
var handOffReceiver = loadedLeague.Teams[0].Players[1];
var handOffReadyMatch = offensiveTurnMatch with
{
    Ball = new BallState { CarrierPlayerId = playerToPlace.Id },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == handOffReceiver.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var handOffService = new MatchService(new FixedDiceRoller(d6: [4]));
var handOffMatch = handOffService.HandOffBall(handOffReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, handOffReceiver.Id);

Assert(handOffMatch.Ball.CarrierPlayerId == handOffReceiver.Id, "successful handoff should transfer the ball");
Assert(handOffMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.HandOff, "handoff should record an activation");
Assert(handOffMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Completed, "handing off should end the carrier's activation");

// A receiver who has already been activated this turn must not be reactivated by catching the ball:
// the catch closes their existing activation so they cannot move again under it.
var alreadyActivatedReceiverMatch = handOffReadyMatch with
{
    Activations =
    [
        new PlayerTurnActivation
        {
            PlayerId = handOffReceiver.Id,
            TeamId = loadedLeague.Teams[0].Id,
            Half = handOffReadyMatch.Half,
            Turn = handOffReadyMatch.Turn,
            Action = PlayerTurnAction.Move
        }
    ]
};
var reactivateService = new MatchService(new FixedDiceRoller(d6: [4]));
var reactivateResult = reactivateService.HandOffBall(alreadyActivatedReceiverMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, handOffReceiver.Id);
Assert(reactivateResult.Ball.CarrierPlayerId == handOffReceiver.Id, "hand-off to an already-activated receiver should still transfer the ball");
Assert(reactivateResult.Activations.Single(a => a.PlayerId == handOffReceiver.Id).Completed, "catching a hand-off must close an already-activated receiver's activation so they cannot move again");

// A player may declare a hand-off without the ball, move to collect it, then hand off to a team-mate.
var pickupHandOffCarrier = playerToPlace;
var pickupHandOffReceiver = loadedLeague.Teams[0].Players[1];
var pickupHandOffBase = offensiveTurnMatch with
{
    Ball = new BallState { Square = new PitchSquare(2, 0) },
    Placements = offensiveTurnMatch.Placements
        .Select(p => p.PlayerId == pickupHandOffCarrier.Id ? p with { Square = new PitchSquare(1, 0), State = PlayerPitchState.Standing }
            : p.PlayerId == pickupHandOffReceiver.Id ? p with { Square = new PitchSquare(4, 0), State = PlayerPitchState.Standing }
            : p)
        .ToArray()
};
var pickupHandOffService = new MatchService(new FixedDiceRoller(d6: [6, 6, 6, 6]));
var pickupHandOffDeclared = pickupHandOffService.DeclarePlayerAction(pickupHandOffBase, loadedLeague.Teams[0], pickupHandOffCarrier.Id, PlayerTurnAction.HandOff);
var pickupHandOffMoved = pickupHandOffService.MovePlayerAsHandOff(pickupHandOffDeclared, ruleset, loadedLeague.Teams[0], pickupHandOffCarrier.Id, new PitchSquare(3, 0));
Assert(pickupHandOffMoved.Ball.CarrierPlayerId == pickupHandOffCarrier.Id, "declaring a hand-off then moving onto the ball should let the carrier pick it up");
var pickupHandOffActivation = pickupHandOffMoved.Activations.Single(a => a.PlayerId == pickupHandOffCarrier.Id);
Assert(pickupHandOffActivation is { Action: PlayerTurnAction.HandOff, DeclaredOnly: false, Completed: false }, "moving after a declared hand-off should keep the hand-off activation live so the carrier can still hand off");
var pickupHandOffResult = pickupHandOffService.HandOffBall(pickupHandOffMoved, ruleset, loadedLeague.Teams[0], pickupHandOffCarrier.Id, pickupHandOffReceiver.Id);
Assert(pickupHandOffResult.Ball.CarrierPlayerId == pickupHandOffReceiver.Id, "handing off after collecting the ball should transfer it to the team-mate");

// A pickup that needs a reroll mid-hand-off must keep the hand-off activation live after the reroll
// resolves, so the carrier can still complete the hand-off (the UI's mode toggle is cleared by the
// pending reroll prompt, so the hand-off must be recoverable from the activation itself).
var rerollHandOffService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6]));
var rerollHandOffMoved = rerollHandOffService.MovePlayerAsHandOff(
    pickupHandOffDeclared with { Weather = WeatherCondition.PouringRain },
    ruleset, loadedLeague.Teams[0], pickupHandOffCarrier.Id, new PitchSquare(2, 0));
Assert(rerollHandOffMoved.PendingReroll?.Kind == PendingRerollKind.Pickup, "a failed hand-off pickup should prompt a reroll");
var rerollHandOffResolved = rerollHandOffService.ResolvePendingReroll(rerollHandOffMoved, ruleset, loadedLeague.Teams[0], useTeamReroll: true);
Assert(rerollHandOffResolved.Ball.CarrierPlayerId == pickupHandOffCarrier.Id, "a successful pickup reroll should leave the carrier holding the ball");
Assert(rerollHandOffResolved.Activations.Single(a => a.PlayerId == pickupHandOffCarrier.Id).Action == PlayerTurnAction.HandOff, "a pickup reroll during a hand-off should keep the hand-off activation");

var failedHandOffService = new MatchService(new FixedDiceRoller(d6: [1], d8: [5]));
var failedHandOffMatch = failedHandOffService.HandOffBall(handOffReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, handOffReceiver.Id);
Assert(failedHandOffMatch.PendingReroll?.Kind == PendingRerollKind.Catch, "a failed hand-off catch should offer a reroll before resolving");
failedHandOffMatch = failedHandOffService.ResolvePendingReroll(failedHandOffMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false);

Assert(failedHandOffMatch.Phase == MatchPhase.DefensiveTurn, "failed offensive handoff should cause a turnover");
Assert(failedHandOffMatch.Ball.CarrierPlayerId is null, "failed handoff should leave the ball loose");
Assert(failedHandOffMatch.Ball.Square == new PitchSquare(3, 1), "failed handoff should scatter from the receiver");

var bounceReceiver = loadedLeague.Teams[0].Players[2];
var friendlyBounceMatch = handOffReadyMatch with
{
    Placements = handOffReadyMatch.Placements
        .Select(placement => placement.PlayerId == bounceReceiver.Id
            ? placement with { Square = new PitchSquare(3, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var friendlyBounceService = new MatchService(new FixedDiceRoller(d6: [1, 4], d8: [5]));
var friendlyBounceResult = friendlyBounceService.HandOffBall(friendlyBounceMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, handOffReceiver.Id);
friendlyBounceResult = friendlyBounceService.ResolvePendingReroll(friendlyBounceResult, ruleset, loadedLeague.Teams[0], useTeamReroll: false);

Assert(friendlyBounceResult.Phase == MatchPhase.OffensivePlayerTurn, "friendly catch on a handoff bounce should avoid turnover");
Assert(friendlyBounceResult.Ball.CarrierPlayerId == bounceReceiver.Id, "friendly bounce catch should recover the ball");

var chainBounceService = new MatchService(new FixedDiceRoller(d6: [1, 1, 4], d8: [5, 4]));
var chainBounceResult = chainBounceService.HandOffBall(friendlyBounceMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, handOffReceiver.Id);
// Decline the hand-off catch, then decline the intervening bounce catch the active team is offered.
chainBounceResult = chainBounceService.ResolvePendingReroll(chainBounceResult, ruleset, loadedLeague.Teams[0], useTeamReroll: false);
Assert(chainBounceResult.PendingReroll?.Kind == PendingRerollKind.Catch, "a failed bounce catch by the active team should also offer a reroll");
chainBounceResult = chainBounceService.ResolvePendingReroll(chainBounceResult, ruleset, loadedLeague.Teams[0], useTeamReroll: false);

Assert(chainBounceResult.Phase == MatchPhase.OffensivePlayerTurn, "eventual friendly catch after chained bounces should avoid turnover");
Assert(chainBounceResult.Ball.CarrierPlayerId == handOffReceiver.Id, "ball can bounce back to original receiver and be caught");

// A team reroll may rescue a dropped bounce catch made by the active team during its turn.
var bounceRerollService = new MatchService(new FixedDiceRoller(d6: [1, 1, 6], d8: [5]));
var bounceRerollResult = bounceRerollService.HandOffBall(friendlyBounceMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, handOffReceiver.Id);
bounceRerollResult = bounceRerollService.ResolvePendingReroll(bounceRerollResult, ruleset, loadedLeague.Teams[0], useTeamReroll: false);
Assert(bounceRerollResult.PendingReroll?.Kind == PendingRerollKind.Catch && bounceRerollResult.PendingReroll.PlayerId == bounceReceiver.Id, "the dropped bounce catch should offer the catching player a reroll");
bounceRerollResult = bounceRerollService.ResolvePendingReroll(bounceRerollResult, ruleset, loadedLeague.Teams[0], useTeamReroll: true);
Assert(bounceRerollResult.Ball.CarrierPlayerId == bounceReceiver.Id, "a successful team reroll on a bounce catch should secure the ball");
Assert(bounceRerollResult.Phase == MatchPhase.OffensivePlayerTurn, "rescuing a bounce catch with a team reroll should avoid the turnover");

var passerPlayer = loadedLeague.Teams[0].Players[6];
var passReceiver = loadedLeague.Teams[0].Players[7];
var passReadyMatch = offensiveTurnMatch with
{
    Ball = new BallState { CarrierPlayerId = passerPlayer.Id },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == passerPlayer.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == passReceiver.Id
                ? placement with { Square = new PitchSquare(4, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var passService = new MatchService(new FixedDiceRoller(d6: [2, 3]));
var completedPassMatch = passService.PassBall(passReadyMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);

Assert(completedPassMatch.Ball.CarrierPlayerId == passReceiver.Id, "completed pass should transfer the ball to the receiver");
Assert(completedPassMatch.Activations.Single(activation => activation.PlayerId == passerPlayer.Id).Action == PlayerTurnAction.Pass, "pass should record a pass activation");
Assert(completedPassMatch.Activations.Single(activation => activation.PlayerId == passerPlayer.Id).Completed, "throwing the ball should end the passer's activation");

var declaredPassMatch = matchService.DeclarePlayerAction(passReadyMatch, loadedLeague.Teams[0], passerPlayer.Id, PlayerTurnAction.Pass);
AssertThrows(
    () => matchService.DeclarePlayerAction(declaredPassMatch, loadedLeague.Teams[0], playerToPlace.Id, PlayerTurnAction.Pass),
    "declared pass should reserve the once-per-turn pass action");
var completedDeclaredPassMatch = new MatchService(new FixedDiceRoller(d6: [2, 3])).PassBall(declaredPassMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);
Assert(completedDeclaredPassMatch.Activations.Count(activation => activation.Action == PlayerTurnAction.Pass) == 1, "resolving a declared pass should reuse the declaration");

var declareWithoutBallMatch = offensiveTurnMatch with
{
    Ball = new BallState { Square = new PitchSquare(2, 1) },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == passerPlayer.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == passReceiver.Id
                ? placement with { Square = new PitchSquare(4, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var declareWithoutBallService = new MatchService(new FixedDiceRoller(d6: [6, 6, 6]));
var declaredWithoutBall = declareWithoutBallService.DeclarePlayerAction(declareWithoutBallMatch, loadedLeague.Teams[0], passerPlayer.Id, PlayerTurnAction.Pass);
Assert(declaredWithoutBall.Activations.Single(activation => activation.PlayerId == passerPlayer.Id).DeclaredOnly, "a pass should be declarable before the player holds the ball");
var movedToBallMatch = declareWithoutBallService.MovePlayerAsPass(declaredWithoutBall, ruleset, loadedLeague.Teams[0], passerPlayer.Id, new PitchSquare(2, 1), awayLeague.Teams[0]);
Assert(movedToBallMatch.Ball.CarrierPlayerId == passerPlayer.Id, "moving under a declared pass should pick up the ball en route");
var passedAfterPickupMatch = declareWithoutBallService.PassBall(movedToBallMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);
Assert(passedAfterPickupMatch.Ball.CarrierPlayerId == passReceiver.Id, "a player should be able to pass after moving to collect the ball under a declared pass");
Assert(passedAfterPickupMatch.Activations.Count(activation => activation.PlayerId == passerPlayer.Id && activation.Action == PlayerTurnAction.Pass) == 1, "declare, move, and pass should remain a single pass activation");

var failedPassService = new MatchService(new FixedDiceRoller(d6: [1], d8: [5]));
var failedPassMatch = failedPassService.PassBall(passReadyMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);
Assert(failedPassMatch.PendingReroll?.Kind == PendingRerollKind.Pass, "failed pass should offer Pass/team reroll choices before resolving");
failedPassMatch = failedPassService.ResolvePendingReroll(failedPassMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false, opposingTeam: awayLeague.Teams[0]);

Assert(failedPassMatch.Phase == MatchPhase.DefensiveTurn, "fumbled pass should cause a turnover");
Assert(failedPassMatch.Ball.CarrierPlayerId is null, "fumbled pass should leave the ball loose if not recovered");
Assert(failedPassMatch.Ball.Square == new PitchSquare(2, 1), "fumbled pass should bounce from the passer");

var passRerollService = new MatchService(new FixedDiceRoller(d6: [1, 2, 3]));
var passRerollMatch = passRerollService.PassBall(passReadyMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id, usePassSkillReroll: true);

Assert(passRerollMatch.Ball.CarrierPlayerId == passReceiver.Id, "Pass skill should be an optional reroll that can rescue a failed passing test");

// An accurate pass whose catch fails must offer a team reroll (BB2020 lets a team reroll be spent on
// the catch even when the passing test itself was already rerolled with the Pass skill). Strip the
// receiver's skills so the failed catch is not silently rescued by an automatic Catch reroll.
var plainCatchTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == passReceiver.Id ? player with { Skills = [] } : player)
        .ToArray()
};
var catchRerollService = new MatchService(new FixedDiceRoller(d6: [5, 1, 6]));
var catchRerollPending = catchRerollService.PassBall(passReadyMatch, ruleset, plainCatchTeam, passerPlayer.Id, passReceiver.Id);
Assert(catchRerollPending.PendingReroll?.Kind == PendingRerollKind.Catch, "a failed catch on an accurate pass should offer a reroll before resolving");
Assert(catchRerollPending.PendingReroll?.PlayerId == passReceiver.Id, "the catch reroll choice should belong to the receiver");
Assert(catchRerollPending.Ball.CarrierPlayerId is null, "the ball should be in the air while the catch reroll choice is pending");
var catchRerollResolved = catchRerollService.ResolvePendingReroll(catchRerollPending, ruleset, plainCatchTeam, useTeamReroll: true);
Assert(catchRerollResolved.Phase == MatchPhase.OffensivePlayerTurn, "a successful catch reroll should complete the pass without a turnover");
Assert(catchRerollResolved.Ball.CarrierPlayerId == passReceiver.Id, "a successful catch reroll should leave the receiver holding the ball");

// Declining the catch reroll replays the original failure and turns the ball over.
var catchDeclineService = new MatchService(new FixedDiceRoller(d6: [5, 1], d8: [5]));
var catchDeclinePending = catchDeclineService.PassBall(passReadyMatch, ruleset, plainCatchTeam, passerPlayer.Id, passReceiver.Id);
Assert(catchDeclinePending.PendingReroll?.Kind == PendingRerollKind.Catch, "a dropped pass should still offer the catch reroll");
var catchDeclineResolved = catchDeclineService.ResolvePendingReroll(catchDeclinePending, ruleset, plainCatchTeam, useTeamReroll: false);
Assert(catchDeclineResolved.Phase == MatchPhase.DefensiveTurn, "declining the catch reroll on a dropped pass should cause a turnover");
Assert(catchDeclineResolved.Ball.CarrierPlayerId is null, "a declined catch reroll should leave the ball loose");

var emptyTargetPassService = new MatchService(new FixedDiceRoller(d6: [2]));
var emptyTargetPassMatch = emptyTargetPassService.PassBall(passReadyMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, new PitchSquare(4, 2));

Assert(emptyTargetPassMatch.Phase == MatchPhase.DefensiveTurn, "accurate pass to an empty square should cause a turnover if not recovered");
Assert(emptyTargetPassMatch.Ball.Square == new PitchSquare(4, 2), "accurate pass to an empty square should land on the target square");

var sunnyPassService = new MatchService(new FixedDiceRoller(d6: [2]));
var accurateTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == passerPlayer.Id ? player with { Skills = [.. player.Skills, "accurate"] } : player)
        .ToArray()
};
var sunnyPassMatch = sunnyPassService.PassBall(
    passReadyMatch with { Weather = WeatherCondition.VerySunny },
    ruleset,
    accurateTeam,
    passerPlayer.Id,
    passReceiver.Id);

Assert(sunnyPassMatch.Phase == MatchPhase.OffensivePlayerTurn, "accurate should offset very sunny weather on quick and short passes");
Assert(sunnyPassMatch.Ball.CarrierPlayerId == passReceiver.Id, "accurate sunny pass should still be completed");

var markedPasserMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayLeague.Teams[0].Players[1].Id
            ? placement with { Square = new PitchSquare(1, 2), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var markedPasserService = new MatchService(new FixedDiceRoller(d6: [2]));
var nervesTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == passerPlayer.Id ? player with { Skills = [.. player.Skills, "nerves-of-steel"] } : player)
        .ToArray()
};
var markedPasserResult = markedPasserService.PassBall(markedPasserMatch, ruleset, nervesTeam, passerPlayer.Id, passReceiver.Id);

Assert(markedPasserResult.Phase == MatchPhase.OffensivePlayerTurn, "passing skills should let the thrower overcome the marker");
Assert(markedPasserResult.Ball.CarrierPlayerId == passReceiver.Id, "skilled marked passer should still complete the pass");

var safePassService = new MatchService(new FixedDiceRoller(d6: [1]));
var safePassTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == passerPlayer.Id ? player with { Skills = [.. player.Skills, "safe-pass"] } : player)
        .ToArray()
};
var safePassMatch = safePassService.PassBall(passReadyMatch, ruleset, safePassTeam, passerPlayer.Id, passReceiver.Id);

Assert(safePassMatch.Phase == MatchPhase.OffensivePlayerTurn, "Safe Pass should prevent a fumble turnover automatically");
Assert(safePassMatch.Ball.CarrierPlayerId == passerPlayer.Id, "Safe Pass should leave the ball carried by the passer");

var droppedPassService = new MatchService(new FixedDiceRoller(d6: [2, 1], d8: [5]));
var droppedPassMatch = droppedPassService.PassBall(passReadyMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);
if (droppedPassMatch.PendingReroll is not null)
{
    droppedPassMatch = droppedPassService.ResolvePendingReroll(droppedPassMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false);
}

Assert(droppedPassMatch.Phase == MatchPhase.DefensiveTurn, "dropped completed pass should cause a turnover if not recovered");
Assert(droppedPassMatch.Ball.Square == new PitchSquare(5, 1), "dropped pass should bounce from the receiver");

var markedReceiverMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayLeague.Teams[0].Players[1].Id
            ? placement with { Square = new PitchSquare(4, 2), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var markedReceiverService = new MatchService(new FixedDiceRoller(d6: [2, 3], d8: [5]));
var markedReceiverResult = markedReceiverService.PassBall(markedReceiverMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);
if (markedReceiverResult.PendingReroll is not null)
{
    markedReceiverResult = markedReceiverService.ResolvePendingReroll(markedReceiverResult, ruleset, loadedLeague.Teams[0], useTeamReroll: false);
}

Assert(markedReceiverResult.Phase == MatchPhase.DefensiveTurn, "opposing tackle zones on the receiver should make catching harder");
Assert(markedReceiverResult.Ball.Square == new PitchSquare(5, 1), "marked receiver dropped pass should bounce from the receiver");

var passBounceReceiver = loadedLeague.Teams[0].Players[1];
var friendlyPassBounceMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == passBounceReceiver.Id
            ? placement with { Square = new PitchSquare(11, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == passReceiver.Id
                ? placement with { Square = new PitchSquare(10, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var friendlyPassBounceService = new MatchService(new FixedDiceRoller(d6: [2, 3, 4], d8: [5]));
var friendlyPassBounceResult = friendlyPassBounceService.PassBall(friendlyPassBounceMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);
if (friendlyPassBounceResult.PendingReroll is not null)
{
    friendlyPassBounceResult = friendlyPassBounceService.ResolvePendingReroll(friendlyPassBounceResult, ruleset, loadedLeague.Teams[0], useTeamReroll: false, opposingTeam: awayLeague.Teams[0]);
}

Assert(friendlyPassBounceResult.Phase == MatchPhase.OffensivePlayerTurn, "friendly catch on an inaccurate pass scatter should avoid turnover");
Assert(friendlyPassBounceResult.Ball.CarrierPlayerId == passBounceReceiver.Id, "friendly player should be able to recover a scattered pass");

// A dropped pass that bounces onto a standing opponent now lets that opponent attempt the catch.
var opponentBounceCatcher = awayLeague.Teams[0].Players[2];
var opponentBounceAwayTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == opponentBounceCatcher.Id ? player with { Stats = player.Stats with { Agility = 2 } } : player)
        .ToArray()
};
var opponentBounceMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == opponentBounceCatcher.Id
            ? placement with { Square = new PitchSquare(4, 2), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var opponentBounceService = new MatchService(new FixedDiceRoller(d6: [2, 1, 6], d8: [7]));
opponentBounceService.RegisterTeams(loadedLeague.Teams[0], opponentBounceAwayTeam);
var opponentBounceResult = opponentBounceService.PassBall(opponentBounceMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);
if (opponentBounceResult.PendingReroll is not null)
{
    opponentBounceResult = opponentBounceService.ResolvePendingReroll(opponentBounceResult, ruleset, loadedLeague.Teams[0], useTeamReroll: false, opposingTeam: opponentBounceAwayTeam);
}

Assert(opponentBounceResult.Ball.CarrierPlayerId == opponentBounceCatcher.Id, "a dropped pass that bounces onto a standing opponent should let them catch it");
Assert(opponentBounceResult.Phase == MatchPhase.DefensiveTurn, "an opponent catching a dropped pass should be a turnover");

var interceptionMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayPlayerToPlace.Id
            ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var interceptionService = new MatchService(new FixedDiceRoller(d6: [3, 6]));
var interceptedPassMatch = interceptionService.PassBall(interceptionMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id, awayLeague.Teams[0]);

Assert(interceptedPassMatch.Phase == MatchPhase.DefensiveTurn, "successful interception should cause a turnover");
Assert(interceptedPassMatch.ActiveTeamId == awayLeague.Teams[0].Id, "intercepting team should become active after turnover");
Assert(interceptedPassMatch.Ball.CarrierPlayerId == awayPlayerToPlace.Id, "interceptor should carry the ball");
Assert(interceptedPassMatch.PlayerAwards.Any(award => award.Kind == MatchPlayerAwardKind.Deflection && award.PlayerId == awayPlayerToPlace.Id), "a caught interference should award the deflector a Deflection (BB2020)");
Assert(interceptedPassMatch.PlayerAwards.Any(award => award.Kind == MatchPlayerAwardKind.Interception && award.PlayerId == awayPlayerToPlace.Id), "catching a deflected pass should also award an Interception");

// Interference succeeds (6) but the deflecting player fails the follow-up catch (1): a Deflection
// is still earned, no Interception, and the ball comes loose rather than being held.
var deflectedOnlyService = new MatchService(new FixedDiceRoller(d6: [3, 6, 1]));
var deflectedOnlyMatch = deflectedOnlyService.PassBall(interceptionMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id, awayLeague.Teams[0]);

Assert(deflectedOnlyMatch.PlayerAwards.Any(award => award.Kind == MatchPlayerAwardKind.Deflection && award.PlayerId == awayPlayerToPlace.Id), "a successful interference should award a Deflection even when the catch fails");
Assert(!deflectedOnlyMatch.PlayerAwards.Any(award => award.Kind == MatchPlayerAwardKind.Interception), "a deflected pass that is not caught should not award an Interception (the ball comes loose to bounce)");

var markedInterceptionService = new MatchService(new FixedDiceRoller(d6: [3, 5, 4]));
var markedInterceptionResult = markedInterceptionService.PassBall(interceptionMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id, awayLeague.Teams[0]);

Assert(markedInterceptionResult.Ball.CarrierPlayerId == passReceiver.Id, "opposing tackle zones on the interceptor should make interception harder");

var secondInterceptor = awayLeague.Teams[0].Players[1];
var multiInterceptionMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayPlayerToPlace.Id
            ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == secondInterceptor.Id
                ? placement with { Square = new PitchSquare(3, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var pendingInterceptionService = new MatchService(new FixedDiceRoller(d6: [3, 1, 4]));
var pendingInterceptionMatch = pendingInterceptionService.PassBall(multiInterceptionMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id, awayLeague.Teams[0]);

Assert(pendingInterceptionMatch.PendingInterception?.EligiblePlayerIds.SequenceEqual([awayPlayerToPlace.Id, secondInterceptor.Id]) == true, "multiple eligible interceptors should require a defensive choice");
Assert(pendingInterceptionMatch.Ball.CarrierPlayerId is null && pendingInterceptionMatch.Ball.Square is null, "pending interception should keep the ball in flight");

var completedAfterFailedInterception = pendingInterceptionService.ChooseInterceptor(pendingInterceptionMatch, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], secondInterceptor.Id);

Assert(completedAfterFailedInterception.PendingInterception is null, "choosing an interceptor should clear the pending choice");
Assert(completedAfterFailedInterception.Ball.CarrierPlayerId == passReceiver.Id, "failed interception should allow the receiver to catch the pass");
Assert(completedAfterFailedInterception.Phase == MatchPhase.OffensivePlayerTurn, "failed interception and completed catch should not cause a turnover");

var cloudBursterTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == passerPlayer.Id ? player with { Skills = [.. player.Skills, "cloud-burster"] } : player)
        .ToArray()
};
var longPassMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == passReceiver.Id
            ? placement with { Square = new PitchSquare(10, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(5, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var cloudDeclinedService = new MatchService(new FixedDiceRoller(d6: [4, 6]));
var cloudDeclinedMatch = cloudDeclinedService.PassBall(longPassMatch, ruleset, cloudBursterTeam, passerPlayer.Id, passReceiver.Id, awayLeague.Teams[0], useCloudBurster: false);

Assert(cloudDeclinedMatch.Ball.CarrierPlayerId == awayPlayerToPlace.Id, "Cloud Burster should be optional, not automatic");

var cloudUsedService = new MatchService(new FixedDiceRoller(d6: [4, 6, 1, 3]));
var cloudUsedMatch = cloudUsedService.PassBall(longPassMatch, ruleset, cloudBursterTeam, passerPlayer.Id, passReceiver.Id, awayLeague.Teams[0], useCloudBurster: true);

Assert(cloudUsedMatch.Ball.CarrierPlayerId == passReceiver.Id, "Cloud Burster should reroll a successful long-pass interference when chosen");

var veryLongLegsInterferenceTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "very-long-legs"] } : player)
        .ToArray()
};
var veryLongLegsInterferenceService = new MatchService(new FixedDiceRoller(d6: [4, 3]));
var veryLongLegsInterferenceMatch = veryLongLegsInterferenceService.PassBall(longPassMatch, ruleset, cloudBursterTeam, passerPlayer.Id, passReceiver.Id, veryLongLegsInterferenceTeam, useCloudBurster: true);

Assert(veryLongLegsInterferenceMatch.Ball.CarrierPlayerId == awayPlayerToPlace.Id, "Very Long Legs should improve interference and ignore Cloud Burster");

var hailMaryTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == passerPlayer.Id ? player with { Skills = [.. player.Skills, "hail-mary-pass"] } : player)
        .ToArray()
};
var hailMaryService = new MatchService(new FixedDiceRoller(d6: [6], d8: [5]));
var hailMaryMatch = hailMaryService.HailMaryPassBall(passReadyMatch, ruleset, hailMaryTeam, passerPlayer.Id, new PitchSquare(20, 5));

Assert(hailMaryMatch.Ball.Square == new PitchSquare(21, 5), "Hail Mary Pass should target anywhere and always resolve as inaccurate at best");

var dumpOffTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == passerPlayer.Id ? player with { Skills = [.. player.Skills, "dump-off"] } : player)
        .ToArray()
};
var dumpOffService = new MatchService(new FixedDiceRoller(d6: [2, 3]));
var dumpOffMatch = dumpOffService.DumpOffPassBall(passReadyMatch with { ActiveTeamId = awayLeague.Teams[0].Id }, ruleset, dumpOffTeam, passerPlayer.Id, new PitchSquare(4, 1), defendingTeam: null);

Assert(dumpOffMatch.Ball.CarrierPlayerId == passReceiver.Id, "Dump-Off should allow a non-active ball carrier to make a Quick Pass without causing a turnover");

var fumblerooskieTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "fumblerooskie"] } : player)
        .ToArray()
};
var fumblerooskieMatch = offensiveTurnMatch with
{
    Ball = new BallState { CarrierPlayerId = playerToPlace.Id },
    Activations =
    [
        new PlayerTurnActivation { PlayerId = playerToPlace.Id, TeamId = loadedLeague.Teams[0].Id, Half = offensiveTurnMatch.Half, Turn = offensiveTurnMatch.Turn, Action = PlayerTurnAction.Move }
    ],
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(2, 0), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var fumblerooskieResult = matchService.UseFumblerooskie(fumblerooskieMatch, ruleset, fumblerooskieTeam, playerToPlace.Id, new PitchSquare(1, 0));

Assert(fumblerooskieResult.Ball.Square == new PitchSquare(1, 0), "Fumblerooskie should place the ball without a bounce or turnover");

var onTheBallTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "on-the-ball"] } : player)
        .ToArray()
};
var onTheBallResult = matchService.MoveOnTheBallPlayer(offensiveTurnMatch, ruleset, onTheBallTeam, playerToPlace.Id, new PitchSquare(2, 0));

Assert(onTheBallResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(2, 0), "On the Ball should move up to three squares");

var runningPassTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == passerPlayer.Id ? player with { Skills = [.. player.Skills, "running-pass"] } : player)
        .ToArray()
};
var runningPassResult = matchService.ContinueRunningPassMove(completedPassMatch, ruleset, runningPassTeam, passerPlayer.Id, new PitchSquare(2, 1));

Assert(runningPassResult.Placements.Single(placement => placement.PlayerId == passerPlayer.Id).Square == new PitchSquare(2, 1), "Running Pass should allow movement to continue after a pass activation");

var disturbingPresenceTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "disturbing-presence"] } : player)
        .ToArray()
};
var disturbingPresenceMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayPlayerToPlace.Id
            ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Prone }
            : placement)
        .ToArray()
};
var disturbingPresenceService = new MatchService(new FixedDiceRoller(d6: [2], d8: [5]));
var disturbingPresenceResult = disturbingPresenceService.PassBall(disturbingPresenceMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id, disturbingPresenceTeam);
if (disturbingPresenceResult.PendingReroll is not null)
{
    disturbingPresenceResult = disturbingPresenceService.ResolvePendingReroll(disturbingPresenceResult, ruleset, loadedLeague.Teams[0], useTeamReroll: false, opposingTeam: disturbingPresenceTeam);
}

Assert(disturbingPresenceResult.Phase == MatchPhase.DefensiveTurn, "Disturbing Presence should modify nearby opposition passing tests even while prone");

var monstrousMouthReceiver = loadedLeague.Teams[0].Players[1];
var monstrousMouthTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == monstrousMouthReceiver.Id ? player with { Skills = [.. player.Skills, "monstrous-mouth"] } : player)
        .ToArray()
};
var monstrousMouthMatch = handOffReadyMatch with
{
    Placements = handOffReadyMatch.Placements
        .Select(placement => placement.PlayerId == monstrousMouthReceiver.Id
            ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var monstrousMouthService = new MatchService(new FixedDiceRoller(d6: [1, 3]));
var monstrousMouthResult = monstrousMouthService.HandOffBall(monstrousMouthMatch, ruleset, monstrousMouthTeam, playerToPlace.Id, monstrousMouthReceiver.Id);
if (monstrousMouthResult.PendingReroll is not null)
{
    monstrousMouthResult = monstrousMouthService.ResolvePendingReroll(monstrousMouthResult, ruleset, monstrousMouthTeam, useTeamReroll: false, skillId: "monstrous-mouth");
}

Assert(monstrousMouthResult.Ball.CarrierPlayerId == monstrousMouthReceiver.Id, "Monstrous Mouth should reroll failed catch attempts");

var bigHandTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "big-hand"] } : player)
        .ToArray()
};
var bigHandMatch = offensiveTurnMatch with
{
    Weather = WeatherCondition.PouringRain,
    Ball = new BallState { Square = new PitchSquare(2, 0) },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == awayPlayerToPlace.Id
            ? placement with { Square = new PitchSquare(3, 0), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var bigHandService = new MatchService(new FixedDiceRoller(d6: [2]));
var bigHandResult = bigHandService.MovePlayer(bigHandMatch, ruleset, bigHandTeam, playerToPlace.Id, new PitchSquare(2, 0), awayLeague.Teams[0]);

Assert(bigHandResult.Ball.CarrierPlayerId == playerToPlace.Id, "Big Hand should ignore marked and pouring-rain pickup modifiers");

var extraArmsTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "extra-arms"] } : player)
        .ToArray()
};
var extraArmsMatch = offensiveTurnMatch with
{
    Weather = WeatherCondition.PouringRain,
    Ball = new BallState { Square = new PitchSquare(2, 0) }
};
var extraArmsService = new MatchService(new FixedDiceRoller(d6: [2]));
var extraArmsResult = extraArmsService.MovePlayer(extraArmsMatch, ruleset, extraArmsTeam, playerToPlace.Id, new PitchSquare(2, 0));

Assert(extraArmsResult.Ball.CarrierPlayerId == playerToPlace.Id, "Extra Arms should improve pickup tests");

smoke.StartSection("Blocking, pushes, armor, and injuries");
var blockReadyMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};

var foulAppearanceTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "foul-appearance"] } : player)
        .ToArray()
};
var foulAppearanceService = new MatchService(new FixedDiceRoller(d6: [1]));
var foulAppearanceResult = foulAppearanceService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, foulAppearanceTeam, awayPlayerToPlace.Id);

Assert(foulAppearanceResult.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Block, "Foul Appearance should waste the declared block action");
Assert(foulAppearanceResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Standing, "Foul Appearance roll of 1 should prevent the block");

var hornsTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "horns"] } : player)
        .ToArray()
};
var hornsDefenderTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Stats = player.Stats with { Strength = 4 } } : player)
        .ToArray()
};
var hornsBlitzMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(3, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var hornsService = new MatchService(new FixedDiceRoller(d6: [6]));
var hornsResult = hornsService.BlitzPlayer(hornsBlitzMatch, ruleset, hornsTeam, playerToPlace.Id, new PitchSquare(2, 1), hornsDefenderTeam, awayPlayerToPlace.Id);

Assert(hornsResult.PendingBlock?.Rolls.Count == 1, "Horns should add strength before assists during a blitz block");

var clawsTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "claws"] } : player)
        .ToArray()
};
var highArmorTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Stats = player.Stats with { Armor = 10 } } : player)
        .ToArray()
};
var clawsService = new MatchService(new FixedDiceRoller(d6: [6, 4, 4, 1, 1]));
var clawsResult = clawsService.BlockPlayer(blockReadyMatch, ruleset, clawsTeam, playerToPlace.Id, highArmorTeam, awayPlayerToPlace.Id);
clawsResult = ConfirmBlockDie(clawsService, clawsResult, ruleset, clawsTeam, highArmorTeam);
clawsResult = clawsService.ChoosePushSquare(clawsResult, ruleset, clawsTeam, highArmorTeam, new PitchSquare(3, 1));

Assert(clawsResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Stunned, "Claws should break armor on an unmodified 8+ block armor roll");

var ironHardSkinTeam = highArmorTeam with
{
    Players = highArmorTeam.Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "iron-hard-skin"] } : player)
        .ToArray()
};
var ironHardSkinService = new MatchService(new FixedDiceRoller(d6: [6, 4, 4]));
var ironHardSkinResult = ironHardSkinService.BlockPlayer(blockReadyMatch, ruleset, clawsTeam, playerToPlace.Id, ironHardSkinTeam, awayPlayerToPlace.Id);
ironHardSkinResult = ConfirmBlockDie(ironHardSkinService, ironHardSkinResult, ruleset, clawsTeam, ironHardSkinTeam);
ironHardSkinResult = ironHardSkinService.ChoosePushSquare(ironHardSkinResult, ruleset, clawsTeam, ironHardSkinTeam, new PitchSquare(3, 1));

Assert(ironHardSkinResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "Iron Hard Skin should prevent Claws from breaking armor");

var tentaclesTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "tentacles"] } : player)
        .ToArray()
};
var tentaclesMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var tentaclesService = new MatchService(new FixedDiceRoller(d6: [6, 6]));
var tentaclesResult = tentaclesService.MovePlayer(tentaclesMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new PitchSquare(1, 2), tentaclesTeam);

Assert(tentaclesResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 1), "Tentacles should hold a marked opponent in place on a successful roll");

var prehensileTailTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "prehensile-tail"] } : player)
        .ToArray()
};
var prehensileTailService = new MatchService(new FixedDiceRoller(d6: [3]));
var prehensileTailResult = prehensileTailService.MovePlayer(tentaclesMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new PitchSquare(1, 2), prehensileTailTeam);

Assert(prehensileTailResult.PendingReroll?.Kind == PendingRerollKind.Dodge && prehensileTailResult.PendingReroll.Target == 4, "Prehensile Tail should worsen a dodge away by one");

var twoHeadsTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "two-heads"] } : player)
        .ToArray()
};
var twoHeadsService = new MatchService(new FixedDiceRoller(d6: [3]));
var twoHeadsResult = twoHeadsService.MovePlayer(tentaclesMatch, ruleset, twoHeadsTeam, playerToPlace.Id, new PitchSquare(1, 2), prehensileTailTeam);

Assert(twoHeadsResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 2), "Two Heads should improve dodge tests");

var divingTackleTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "diving-tackle"] } : player)
        .ToArray()
};
var divingTackleMatch = tentaclesMatch with
{
    Placements = tentaclesMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var divingTackleService = new MatchService(new FixedDiceRoller(d6: [3]));
var divingTacklePending = divingTackleService.MovePlayer(divingTackleMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new PitchSquare(0, 1), divingTackleTeam);

Assert(divingTacklePending.PendingDivingTackle?.TacklerPlayerId == awayPlayerToPlace.Id, "Diving Tackle should create a defender choice when it can change a successful dodge into a failure");
AssertThrows(
    () => divingTackleService.AdvanceTurn(divingTacklePending, ruleset),
    "pending Diving Tackle should block turn advancement");

var declinedDivingTackle = divingTackleService.ResolvePendingDivingTackle(divingTacklePending, ruleset, loadedLeague.Teams[0], divingTackleTeam, useDivingTackle: false);
Assert(declinedDivingTackle.PendingDivingTackle is null, "declining Diving Tackle should clear the pending choice");
Assert(declinedDivingTackle.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(0, 1), "declining Diving Tackle should let the dodge succeed");
Assert(declinedDivingTackle.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Standing, "declining Diving Tackle should leave the tackler standing");

var usedDivingTackle = divingTackleService.ResolvePendingDivingTackle(divingTacklePending, ruleset, loadedLeague.Teams[0], divingTackleTeam, useDivingTackle: true);
Assert(usedDivingTackle.PendingReroll?.Kind == PendingRerollKind.Dodge && usedDivingTackle.PendingReroll.Target == 4, "using Diving Tackle should turn the dodge into a failed dodge with reroll options");
Assert(usedDivingTackle.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "using Diving Tackle should place the tackler prone");

// BB2020: Diving Tackle also applies to a successful Leap that leaves the tackler's zone.
var leapTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "leap"] } : player)
        .ToArray()
};
var leapDivingTackleService = new MatchService(new FixedDiceRoller(d6: [5, 2, 2]));
var leapDivingTacklePending = leapDivingTackleService.LeapPlayer(divingTackleMatch, ruleset, leapTeam, playerToPlace.Id, new PitchSquare(1, 3), divingTackleTeam);
Assert(leapDivingTacklePending.PendingDivingTackle?.IsLeap == true && leapDivingTacklePending.PendingDivingTackle.TacklerPlayerId == awayPlayerToPlace.Id, "Diving Tackle should interrupt a successful leap that leaves the tackler's zone");
AssertThrows(
    () => leapDivingTackleService.AdvanceTurn(leapDivingTacklePending, ruleset),
    "pending Diving Tackle on a leap should block turn advancement");

var declinedLeapDivingTackle = leapDivingTackleService.ResolvePendingDivingTackle(leapDivingTacklePending, ruleset, loadedLeague.Teams[0], divingTackleTeam, useDivingTackle: false);
Assert(declinedLeapDivingTackle.PendingDivingTackle is null, "declining Diving Tackle on a leap should clear the pending choice");
Assert(declinedLeapDivingTackle.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 3), "declining Diving Tackle should let the leap finish");

var usedLeapDivingTackle = leapDivingTackleService.ResolvePendingDivingTackle(leapDivingTacklePending, ruleset, loadedLeague.Teams[0], divingTackleTeam, useDivingTackle: true);
Assert(usedLeapDivingTackle.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "using Diving Tackle on a leap should place the tackler prone");
Assert(usedLeapDivingTackle.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Prone, "the -2 from Diving Tackle should turn the leap into a fall");

// BB2020 On the Ball: a defender with the skill may reposition before the pass is rolled.
var onTheBallAwayTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "on-the-ball"] } : player)
        .ToArray()
};
var onTheBallPassMatch = passReadyMatch with
{
    Placements = passReadyMatch.Placements
        .Select(placement => placement.PlayerId == passerPlayer.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == passReceiver.Id
                ? placement with { Square = new PitchSquare(4, 1), State = PlayerPitchState.Standing }
                : placement.PlayerId == awayPlayerToPlace.Id
                    ? placement with { Square = new PitchSquare(2, 3), State = PlayerPitchState.Standing }
                    : placement with { Square = null, State = PlayerPitchState.Reserve })
        .ToArray()
};
var onTheBallInterruptService = new MatchService(new FixedDiceRoller(d6: [2, 3]));
var onTheBallInterruptPending = onTheBallInterruptService.PassBall(onTheBallPassMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id, onTheBallAwayTeam);
Assert(onTheBallInterruptPending.PendingOnTheBall?.EligiblePlayerIds.Contains(awayPlayerToPlace.Id) == true, "a declared pass should offer On the Ball to a defender with the skill before any roll");
AssertThrows(
    () => onTheBallInterruptService.AdvanceTurn(onTheBallInterruptPending, ruleset),
    "pending On the Ball should block turn advancement");
AssertThrows(
    () => onTheBallInterruptService.ResolvePendingOnTheBall(onTheBallInterruptPending, ruleset, onTheBallAwayTeam, loadedLeague.Teams[0], awayPlayerToPlace.Id, new PitchSquare(2, 4)),
    "On the Ball should reject a move that does not get closer to the pass target");

var declinedOnTheBall = onTheBallInterruptService.ResolvePendingOnTheBall(onTheBallInterruptPending, ruleset, onTheBallAwayTeam, loadedLeague.Teams[0], null, null);
Assert(declinedOnTheBall.PendingOnTheBall is null, "declining On the Ball should clear the pending choice");
Assert(declinedOnTheBall.Ball.CarrierPlayerId == passReceiver.Id, "declining On the Ball should resume and complete the interrupted pass");

var onTheBallMoveService = new MatchService(new FixedDiceRoller(d6: [2, 3]));
var onTheBallMovePending = onTheBallMoveService.PassBall(onTheBallPassMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id, onTheBallAwayTeam);
var movedOnTheBall = onTheBallMoveService.ResolvePendingOnTheBall(onTheBallMovePending, ruleset, onTheBallAwayTeam, loadedLeague.Teams[0], awayPlayerToPlace.Id, new PitchSquare(3, 2));
Assert(movedOnTheBall.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).Square == new PitchSquare(3, 2), "On the Ball should reposition the defender closer to the pass target");
Assert(movedOnTheBall.PendingOnTheBall is null, "moving an On the Ball player should resume the interrupted pass");

// BB2020 Dump-Off: a block on a ball carrier with the skill offers a Quick Pass before the block dice.
var dumpOffDefenderTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "dump-off"] } : player)
        .ToArray()
};
var dumpOffReceiver = awayLeague.Teams[0].Players[1];
var dumpOffBlockMatch = blockReadyMatch with
{
    Ball = new BallState { CarrierPlayerId = awayPlayerToPlace.Id },
    Placements = blockReadyMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement.PlayerId == dumpOffReceiver.Id
                    ? placement with { Square = new PitchSquare(4, 1), State = PlayerPitchState.Standing }
                    : placement with { Square = null, State = PlayerPitchState.Reserve })
        .ToArray()
};
var dumpOffInterruptService = new MatchService(new FixedDiceRoller(d6: [6, 6, 6, 6, 6]));
var dumpOffInterruptPending = dumpOffInterruptService.BlockPlayer(dumpOffBlockMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, dumpOffDefenderTeam, awayPlayerToPlace.Id);
Assert(dumpOffInterruptPending.PendingDumpOff?.CarrierPlayerId == awayPlayerToPlace.Id, "a block on a Dump-Off carrier should offer a Quick Pass before the block dice");
AssertThrows(
    () => dumpOffInterruptService.AdvanceTurn(dumpOffInterruptPending, ruleset),
    "pending Dump-Off should block turn advancement");

var thrownDumpOff = dumpOffInterruptService.ResolvePendingDumpOff(dumpOffInterruptPending, ruleset, dumpOffDefenderTeam, loadedLeague.Teams[0], new PitchSquare(4, 1));
Assert(thrownDumpOff.Ball.CarrierPlayerId == dumpOffReceiver.Id, "Dump-Off should complete the Quick Pass to the receiver");
Assert(thrownDumpOff.PendingDumpOff is null && thrownDumpOff.DeferredBlock is null, "throwing Dump-Off should resume the held block once the pass settles");

var declinedDumpOffService = new MatchService(new FixedDiceRoller(d6: [6, 6, 6, 6, 6]));
var declinedDumpOffPending = declinedDumpOffService.BlockPlayer(dumpOffBlockMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, dumpOffDefenderTeam, awayPlayerToPlace.Id);
var declinedDumpOff = declinedDumpOffService.ResolvePendingDumpOff(declinedDumpOffPending, ruleset, dumpOffDefenderTeam, loadedLeague.Teams[0], targetSquare: null);
Assert(declinedDumpOff.PendingDumpOff is null && declinedDumpOff.DeferredBlock is null, "declining Dump-Off should resume the held block");
Assert(declinedDumpOff.Ball.CarrierPlayerId == awayPlayerToPlace.Id, "declining Dump-Off should leave the ball with the carrier");

var titchyMarkerTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "titchy"] } : player)
        .ToArray()
};
var titchyMarkerResult = new MatchService(new FixedDiceRoller(d6: [1])).MovePlayer(divingTackleMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new PitchSquare(0, 1), titchyMarkerTeam);
Assert(titchyMarkerResult.PendingReroll is null && titchyMarkerResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(0, 1), "Titchy players should not project tackle zones that force dodges");

var blockService = new MatchService(new FixedDiceRoller(d6: [6, 1, 1]));
var blockMatch = blockService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
blockMatch = ConfirmBlockDie(blockService, blockMatch, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);

Assert(blockMatch.PendingPush?.KnockDefenderDown == true, "successful block should ask for a push square before knocking the defender down");
blockMatch = blockService.ChoosePushSquare(blockMatch, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));
var blockedPlayer = blockMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);

Assert(blockedPlayer.State == PlayerPitchState.Prone, "successful block should knock defender down after the push");
Assert(blockedPlayer.Square == new PitchSquare(3, 1), "successful block should push the defender before knockdown");
Assert(blockMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Block, "block should activate the attacker");

var skullBlockRerollService = new MatchService(new FixedDiceRoller(d6: [1, 6]));
var skullBlockPending = skullBlockRerollService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
skullBlockPending = ConfirmBlockDie(skullBlockRerollService, skullBlockPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
Assert(skullBlockPending.PendingBlockReroll?.AttackerPlayerId == playerToPlace.Id, "single-die attacker down should offer a team reroll before turnover");
var skullBlockRerolled = skullBlockRerollService.ResolvePendingBlockReroll(skullBlockPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], useTeamReroll: true);
Assert(skullBlockRerolled.PendingPush?.KnockDefenderDown == true, "successful block reroll should continue normal block resolution");
Assert(skullBlockRerolled.HomeRerollsRemaining == loadedLeague.Teams[0].Rerolls - 1, "block team reroll should spend a team reroll");

var stumbleService = new MatchService(new FixedDiceRoller(d6: [5]));
var stumbleMatch = stumbleService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
stumbleMatch = ConfirmBlockDie(stumbleService, stumbleMatch, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
Assert(stumbleMatch.PendingPush?.KnockDefenderDown == true, "Defender Stumbles against a defender without Dodge should knock the defender down");

var dodgeStumbleDefenderTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "dodge"] } : player)
        .ToArray()
};
var dodgeStumbleService = new MatchService(new FixedDiceRoller(d6: [5]));
var dodgeStumbleMatch = dodgeStumbleService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, dodgeStumbleDefenderTeam, awayPlayerToPlace.Id);
dodgeStumbleMatch = ConfirmBlockDie(dodgeStumbleService, dodgeStumbleMatch, ruleset, loadedLeague.Teams[0], dodgeStumbleDefenderTeam);
Assert(dodgeStumbleMatch.PendingPush?.KnockDefenderDown == false, "Defender Stumbles against a defender with Dodge should push without knocking down");

var tackleStumbleAttackerTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "tackle"] } : player)
        .ToArray()
};
var tackleStumbleService = new MatchService(new FixedDiceRoller(d6: [5]));
var tackleStumbleMatch = tackleStumbleService.BlockPlayer(blockReadyMatch, ruleset, tackleStumbleAttackerTeam, playerToPlace.Id, dodgeStumbleDefenderTeam, awayPlayerToPlace.Id);
tackleStumbleMatch = ConfirmBlockDie(tackleStumbleService, tackleStumbleMatch, ruleset, tackleStumbleAttackerTeam, dodgeStumbleDefenderTeam);
Assert(tackleStumbleMatch.PendingPush?.KnockDefenderDown == true, "Tackle should negate Dodge against a Defender Stumbles result");

var standFirmTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "stand-firm"] } : player)
        .ToArray()
};
var standFirmService = new MatchService(new FixedDiceRoller(d6: [3]));
var standFirmMatch = standFirmService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, standFirmTeam, awayPlayerToPlace.Id);
standFirmMatch = ConfirmBlockDie(standFirmService, standFirmMatch, ruleset, loadedLeague.Teams[0], standFirmTeam);

Assert(standFirmMatch.PendingStandFirm?.DefenderPlayerId == awayPlayerToPlace.Id, "stand firm should create a defender choice");
AssertThrows(
    () => standFirmService.AdvanceTurn(standFirmMatch, ruleset),
    "pending stand firm should block turn advancement");
var stoodFirmMatch = standFirmService.ResolvePendingStandFirm(standFirmMatch, ruleset, loadedLeague.Teams[0], standFirmTeam, useStandFirm: true);

Assert(stoodFirmMatch.PendingPush is null, "using stand firm should prevent choosing a push square");
Assert(stoodFirmMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).Square == new PitchSquare(2, 1), "using stand firm should keep the defender in place");

var declinedStandFirmMatch = standFirmService.ResolvePendingStandFirm(standFirmMatch, ruleset, loadedLeague.Teams[0], standFirmTeam, useStandFirm: false);
Assert(declinedStandFirmMatch.PendingPush?.DefenderPlayerId == awayPlayerToPlace.Id, "declining stand firm should continue into the normal push choice");

var pushBlockService = new MatchService(new FixedDiceRoller(d6: [3]));
var pendingPushBlock = pushBlockService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
pendingPushBlock = ConfirmBlockDie(pushBlockService, pendingPushBlock, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);

Assert(pendingPushBlock.PendingPush?.KnockDefenderDown == false, "push result should ask for a push square without knockdown");
AssertThrows(
    () => pushBlockService.AdvanceTurn(pendingPushBlock, ruleset),
    "pending push should block turn advancement");
AssertThrows(
    () => pushBlockService.DeclarePlayerAction(pendingPushBlock, loadedLeague.Teams[0], loadedLeague.Teams[0].Players[1].Id, PlayerTurnAction.Move),
    "pending push should block new action declarations");
var pushedBlock = pushBlockService.ChoosePushSquare(pendingPushBlock, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));
var pushedDefender = pushedBlock.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);

Assert(pushedDefender.State == PlayerPitchState.Standing, "push result should leave the defender standing");
Assert(pushedDefender.Square == new PitchSquare(3, 1), "push result should move the defender to the chosen square");
Assert(pushedBlock.PendingFollowUp?.FollowUpSquare == new PitchSquare(2, 1), "push result should offer the attacker a follow-up choice");
Assert(pushedBlock.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Completed == false, "a blocker awaiting a follow-up choice should not yet be marked done for the turn");
var followedUpBlock = pushBlockService.ResolvePendingFollowUp(pushedBlock, loadedLeague.Teams[0], awayLeague.Teams[0], useFollowUp: true);
Assert(followedUpBlock.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(2, 1), "choosing to follow up should move the attacker into the defender's original square");
Assert(followedUpBlock.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Completed, "following up after a block should end the blocker's activation");
var declinedFollowUpBlock = pushBlockService.ResolvePendingFollowUp(pushedBlock, loadedLeague.Teams[0], awayLeague.Teams[0], useFollowUp: false);
Assert(declinedFollowUpBlock.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Completed, "declining a follow-up after a block should still end the blocker's activation");

var fendTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "fend"] } : player)
        .ToArray()
};
var fendService = new MatchService(new FixedDiceRoller(d6: [3]));
var pendingFendPush = fendService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, fendTeam, awayPlayerToPlace.Id);
pendingFendPush = ConfirmBlockDie(fendService, pendingFendPush, ruleset, loadedLeague.Teams[0], fendTeam);
var fendPush = fendService.ChoosePushSquare(pendingFendPush, ruleset, loadedLeague.Teams[0], fendTeam, new PitchSquare(3, 1));
Assert(fendPush.PendingFollowUp is null, "Fend should prevent the attacker follow-up choice after a push");
Assert(fendPush.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 1), "Fend should leave the attacker in place");

var frenzyTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "frenzy"] } : player)
        .ToArray()
};
var frenzyService = new MatchService(new FixedDiceRoller(d6: [3, 3]));
var pendingFrenzyPush = frenzyService.BlockPlayer(blockReadyMatch, ruleset, frenzyTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
pendingFrenzyPush = ConfirmBlockDie(frenzyService, pendingFrenzyPush, ruleset, frenzyTeam, awayLeague.Teams[0]);
var frenzySecondBlock = frenzyService.ChoosePushSquare(pendingFrenzyPush, ruleset, frenzyTeam, awayLeague.Teams[0], new PitchSquare(3, 1));
frenzySecondBlock = ConfirmBlockDie(frenzyService, frenzySecondBlock, ruleset, frenzyTeam, awayLeague.Teams[0]);
Assert(frenzySecondBlock.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(2, 1), "Frenzy should force the attacker to follow up after the first push");
Assert(frenzySecondBlock.PendingPush?.DefenderPlayerId == awayPlayerToPlace.Id, "Frenzy should immediately resolve a second block when the defender remains standing and adjacent");
Assert(frenzySecondBlock.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).BlocksMade == 1, "Frenzy second block should be tracked as part of the same block activation before its push is resolved");

var chainPushedPlayer = awayLeague.Teams[0].Players[1];
var secondChainPushedPlayer = awayLeague.Teams[0].Players[2];
var thirdChainPushedPlayer = awayLeague.Teams[0].Players[3];
var chainPushMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement.PlayerId == chainPushedPlayer.Id
                    ? placement with { Square = new PitchSquare(3, 1), State = PlayerPitchState.Standing }
                    : placement.PlayerId == secondChainPushedPlayer.Id
                        ? placement with { Square = new PitchSquare(3, 0), State = PlayerPitchState.Standing }
                        : placement.PlayerId == thirdChainPushedPlayer.Id
                            ? placement with { Square = new PitchSquare(3, 2), State = PlayerPitchState.Standing }
                    : placement)
        .ToArray()
};
var chainPushService = new MatchService(new FixedDiceRoller(d6: [3]));
var pendingChainPush = chainPushService.BlockPlayer(chainPushMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
pendingChainPush = ConfirmBlockDie(chainPushService, pendingChainPush, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);

Assert(pendingChainPush.PendingPush?.LegalSquares.Contains(new PitchSquare(3, 1)) == true, "occupied push squares should be legal only when no unoccupied on-pitch push square exists");
var chainPushResult = chainPushService.ChoosePushSquare(pendingChainPush, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));
Assert(chainPushResult.PendingPush?.DefenderPlayerId == chainPushedPlayer.Id, "chain push should ask where the occupying player is pushed");
chainPushResult = chainPushService.ChoosePushSquare(chainPushResult, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(4, 0));

Assert(chainPushResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).Square == new PitchSquare(3, 1), "push into an occupied square should move the original pushed player there");
Assert(chainPushResult.Placements.Single(placement => placement.PlayerId == chainPushedPlayer.Id).Square == new PitchSquare(4, 0), "push into an occupied square should chain-push the occupying player");

var fourthChainPushedPlayer = awayLeague.Teams[0].Players[4];
var cascadePushMatch = chainPushMatch with
{
    Placements = chainPushMatch.Placements
        .Select(placement => placement.PlayerId == fourthChainPushedPlayer.Id
            ? placement with { Square = new PitchSquare(4, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var cascadePushService = new MatchService(new FixedDiceRoller(d6: [3]));
var pendingCascadePush = cascadePushService.BlockPlayer(cascadePushMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
pendingCascadePush = ConfirmBlockDie(cascadePushService, pendingCascadePush, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
var cascadePushResult = cascadePushService.ChoosePushSquare(pendingCascadePush, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));
if (cascadePushResult.PendingPush is not null)
{
    cascadePushResult = cascadePushService.ChoosePushSquare(cascadePushResult, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(4, 0));
}

Assert(cascadePushResult.Placements.Single(placement => placement.PlayerId == chainPushedPlayer.Id).Square == new PitchSquare(4, 0), "cascade chain push should force the second pushed player into an unoccupied square before occupied options");
Assert(cascadePushResult.Placements.Single(placement => placement.PlayerId == fourthChainPushedPlayer.Id).Square == new PitchSquare(4, 1), "cascade chain push should not push a third player while the second player has an unoccupied destination");

var emptyPreferredPushMatch = chainPushMatch with
{
    Placements = chainPushMatch.Placements
        .Select(placement => placement.PlayerId == secondChainPushedPlayer.Id
            ? placement with { Square = null, State = PlayerPitchState.Reserve }
            : placement)
        .ToArray()
};
var emptyPreferredPushService = new MatchService(new FixedDiceRoller(d6: [3]));
var emptyPreferredPush = emptyPreferredPushService.BlockPlayer(emptyPreferredPushMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
emptyPreferredPush = ConfirmBlockDie(emptyPreferredPushService, emptyPreferredPush, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);

Assert(emptyPreferredPush.PendingPush is null, "single unoccupied legal push square should resolve automatically instead of offering occupied chain-push squares");
Assert(emptyPreferredPush.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).Square == new PitchSquare(3, 0), "unoccupied push square must be chosen before occupied chain-push squares");
Assert(emptyPreferredPush.Placements.Single(placement => placement.PlayerId == chainPushedPlayer.Id).Square == new PitchSquare(3, 1), "occupied chain-push square should not be used while an unoccupied push square exists");

var crowdPushMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(0, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var crowdPushService = new MatchService(new FixedDiceRoller(d6: [3, 1, 2]));
var crowdPushResult = crowdPushService.BlockPlayer(crowdPushMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
crowdPushResult = ConfirmBlockDie(crowdPushService, crowdPushResult, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
var crowdedPlayer = crowdPushResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);

Assert(crowdedPlayer.Square is null, "sideline push with no legal on-pitch destination should push the player off the pitch");
Assert(crowdedPlayer.State == PlayerPitchState.Reserve, "crowd push with no lasting injury should put the player in reserve");

var bothDownService = new MatchService(new FixedDiceRoller(d6: [2, 1, 1, 1, 1], d8: [5]));
var bothDownPending = bothDownService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
bothDownPending = ConfirmBlockDie(bothDownService, bothDownPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
Assert(bothDownPending.PendingBlockReroll?.AttackerPlayerId == playerToPlace.Id, "single-die both down should offer a team reroll before turnover");
var bothDownMatch = bothDownService.ResolvePendingBlockReroll(bothDownPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], useTeamReroll: false);

Assert(bothDownMatch.Phase == MatchPhase.DefensiveTurn, "both-down block should cause a turnover");
Assert(bothDownMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Prone, "both-down block should knock the attacker down");
Assert(bothDownMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "both-down block should knock the defender down");

var blockSkillBothDownMatch = blockReadyMatch with
{
    Placements = blockReadyMatch.Placements
        .Select(placement => placement.PlayerId == loadedLeague.Teams[0].Players[8].Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == playerToPlace.Id
                ? placement with { Square = new PitchSquare(0, 0), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var blockSkillBothDownService = new MatchService(new FixedDiceRoller(d6: [2, 1, 1]));
var blockSkillBothDownPending = blockSkillBothDownService.BlockPlayer(blockSkillBothDownMatch, ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0].Players[8].Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
blockSkillBothDownPending = ConfirmBlockDie(blockSkillBothDownService, blockSkillBothDownPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
Assert(blockSkillBothDownPending.PendingBlockReroll is null, "Block-protected both down should not ask for a second reroll decision");
var blockSkillBothDownResult = blockSkillBothDownPending;

Assert(blockSkillBothDownResult.Phase == MatchPhase.OffensivePlayerTurn, "block skill should prevent an attacker both-down turnover");
Assert(blockSkillBothDownResult.Placements.Single(placement => placement.PlayerId == loadedLeague.Teams[0].Players[8].Id).State == PlayerPitchState.Standing, "block skill should keep the attacker standing on both down");
Assert(blockSkillBothDownResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "both down should still knock over a defender without block");
Assert(blockSkillBothDownResult.Activations.Single(activation => activation.PlayerId == loadedLeague.Teams[0].Players[8].Id).Completed, "a both-down survived with Block should end the blocker's activation");

var blockSkillCasualtyService = new MatchService(new FixedDiceRoller(d6: [2, 6, 6, 6, 6], d16: [9]));
var blockSkillCasualtyPending = blockSkillCasualtyService.BlockPlayer(blockSkillBothDownMatch, ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0].Players[8].Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
blockSkillCasualtyPending = ConfirmBlockDie(blockSkillCasualtyService, blockSkillCasualtyPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
var blockSkillCasualtyResult = blockSkillCasualtyPending;
var blockSkillCasualtyAward = blockSkillCasualtyResult.PlayerAwards.Single(award => award.Kind == MatchPlayerAwardKind.Casualty);
Assert(blockSkillCasualtyAward.PlayerId == loadedLeague.Teams[0].Players[8].Id && blockSkillCasualtyAward.VictimPlayerId == awayPlayerToPlace.Id, "direct block casualty SPP should credit the blocker and name the victim");
Assert(blockSkillCasualtyAward.PlayerName == loadedLeague.Teams[0].Players[8].Name && blockSkillCasualtyAward.VictimPlayerName == awayPlayerToPlace.Name, "direct block casualty awards should include post-match display names");
Assert(blockSkillCasualtyAward.CasualtyResult == CasualtyResult.SeriouslyHurt && blockSkillCasualtyAward.StarPlayerPoints == 2, "direct block casualty awards should include casualty result and SPP value");

var wrestleTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "wrestle"] } : player)
        .ToArray()
};
var wrestleService = new MatchService(new FixedDiceRoller(d6: [2], d8: [5]));
var wrestleMatch = wrestleService.BlockPlayer(blockReadyMatch with { Ball = new BallState { CarrierPlayerId = playerToPlace.Id } }, ruleset, wrestleTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
wrestleMatch = ConfirmBlockDie(wrestleService, wrestleMatch, ruleset, wrestleTeam, awayLeague.Teams[0]);

Assert(wrestleMatch.Phase == MatchPhase.DefensiveTurn, "using wrestle with the blocking ball carrier should cause a turnover");
Assert(wrestleMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Prone, "wrestle should place the attacker prone");
Assert(wrestleMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "wrestle should place the defender prone");
Assert(wrestleMatch.Ball.CarrierPlayerId is null && wrestleMatch.Ball.Square is not null, "wrestle should drop the ball without keeping it carried");

var defenderCarrierWrestleService = new MatchService(new FixedDiceRoller(d6: [2], d8: [5]));
var defenderCarrierWrestleMatch = defenderCarrierWrestleService.BlockPlayer(blockReadyMatch with { Ball = new BallState { CarrierPlayerId = awayPlayerToPlace.Id } }, ruleset, wrestleTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
defenderCarrierWrestleMatch = ConfirmBlockDie(defenderCarrierWrestleService, defenderCarrierWrestleMatch, ruleset, wrestleTeam, awayLeague.Teams[0]);

Assert(defenderCarrierWrestleMatch.Phase == MatchPhase.OffensivePlayerTurn, "using wrestle against a defending ball carrier should not by itself cause the blocking team to turn over");
Assert(defenderCarrierWrestleMatch.Ball.CarrierPlayerId is null && defenderCarrierWrestleMatch.Ball.Square is not null, "wrestle should drop a defending carrier's ball");
Assert(defenderCarrierWrestleMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Completed, "a both-down resolved with Wrestle should end the blocker's activation");

var strongerAwayTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Stats = player.Stats with { Strength = 4 } } : player)
        .ToArray()
};
var dauntlessTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "dauntless"] } : player)
        .ToArray()
};
var dauntlessService = new MatchService(new FixedDiceRoller(d6: [2, 6, 1]));
var dauntlessBlock = dauntlessService.BlockPlayer(blockReadyMatch, ruleset, dauntlessTeam, playerToPlace.Id, strongerAwayTeam, awayPlayerToPlace.Id);

Assert(dauntlessBlock.PendingBlock?.Rolls.Count == 1, "successful dauntless should treat the blocker as equal raw strength before assists and roll one block die");
dauntlessBlock = ConfirmBlockDie(dauntlessService, dauntlessBlock, ruleset, dauntlessTeam, strongerAwayTeam);
Assert(dauntlessBlock.PendingPush?.KnockDefenderDown == true, "successful dauntless should consume its roll before the one block die is resolved");

// A failed Dauntless roll may be rerolled before the block dice are rolled.
var dauntlessRerollService = new MatchService(new FixedDiceRoller(d6: [1, 6, 4]));
var dauntlessRerollPending = dauntlessRerollService.BlockPlayer(blockReadyMatch, ruleset, dauntlessTeam, playerToPlace.Id, strongerAwayTeam, awayPlayerToPlace.Id);
Assert(dauntlessRerollPending.PendingReroll?.Kind == PendingRerollKind.Dauntless, "a failed Dauntless roll should offer a reroll before the block dice");
var dauntlessRerolled = dauntlessRerollService.ResolvePendingReroll(dauntlessRerollPending, ruleset, dauntlessTeam, useTeamReroll: true, opposingTeam: strongerAwayTeam);
Assert(dauntlessRerolled.PendingBlock?.Rolls.Count == 1, "a successful Dauntless reroll should equalise strength and roll one block die");

var badBlockService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6], d8: [5]));
var badBlockPending = badBlockService.BlockPlayer(
    blockReadyMatch with { Ball = new BallState { CarrierPlayerId = playerToPlace.Id } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    awayLeague.Teams[0],
    awayPlayerToPlace.Id);
badBlockPending = ConfirmBlockDie(badBlockService, badBlockPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
Assert(badBlockPending.PendingBlockReroll is not null, "attacker-down block should offer a reroll before resolving failure");
var badBlockMatch = badBlockService.ResolvePendingBlockReroll(badBlockPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], useTeamReroll: false);
var badBlockAttacker = badBlockMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(badBlockMatch.Phase == MatchPhase.DefensiveTurn, "attacker-down block should cause a turnover");
Assert(badBlockAttacker.State == PlayerPitchState.Casualty, "attacker-down injury roll of 10+ should injure the player");
Assert(badBlockAttacker.Casualty?.Roll == 1 && badBlockAttacker.Casualty.Result == CasualtyResult.BadlyHurt, "injury roll of 10+ should immediately roll on the casualty table");
Assert(badBlockMatch.Ball.Square == new PitchSquare(2, 1), "attacker-down ball carrier should scatter the ball");

var mightyBlowTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "mighty-blow"] } : player)
        .ToArray()
};
var lowArmorAwayTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Stats = player.Stats with { Armor = 8 } } : player)
        .ToArray()
};
var mightyBlowService = new MatchService(new FixedDiceRoller(d6: [6, 4, 4, 4, 4]));
var mightyBlowMatch = mightyBlowService.BlockPlayer(blockReadyMatch, ruleset, mightyBlowTeam, playerToPlace.Id, lowArmorAwayTeam, awayPlayerToPlace.Id);
mightyBlowMatch = ConfirmBlockDie(mightyBlowService, mightyBlowMatch, ruleset, mightyBlowTeam, lowArmorAwayTeam);
mightyBlowMatch = mightyBlowService.ChoosePushSquare(mightyBlowMatch, ruleset, mightyBlowTeam, lowArmorAwayTeam, new PitchSquare(3, 1));
var mightyBlowVictim = mightyBlowMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);

Assert(mightyBlowVictim.State == PlayerPitchState.KnockedOut, "mighty blow should be able to turn an armor tie into an armor break");

var deathBlockService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6], d16: [16]));
var deathBlockPending = deathBlockService.BlockPlayer(
    blockReadyMatch,
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    awayLeague.Teams[0],
    awayPlayerToPlace.Id);
deathBlockPending = ConfirmBlockDie(deathBlockService, deathBlockPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
var deathBlockMatch = deathBlockService.ResolvePendingBlockReroll(deathBlockPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], useTeamReroll: false);
var deadAttacker = deathBlockMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(deadAttacker.State == PlayerPitchState.Dead, "dead should come from the casualty table rather than the injury roll");
Assert(deadAttacker.Casualty?.Result == CasualtyResult.Dead, "casualty roll of 15-16 should be dead");

var apothecaryBlockService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6], d16: [16, 1]));
var apothecaryBlockPending = apothecaryBlockService.BlockPlayer(
    blockReadyMatch with { HomeApothecariesRemaining = 1 },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    awayLeague.Teams[0],
    awayPlayerToPlace.Id);
apothecaryBlockPending = ConfirmBlockDie(apothecaryBlockService, apothecaryBlockPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
var apothecaryBlockMatch = apothecaryBlockService.ResolvePendingBlockReroll(apothecaryBlockPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], useTeamReroll: false);
Assert(apothecaryBlockMatch.PendingApothecary?.PlayerId == playerToPlace.Id, "casualty with an available apothecary should create a pending choice");
Assert(apothecaryBlockMatch.HomeApothecariesRemaining == 1, "apothecary should not be spent before the user chooses to use it");
var apothecaryUsedMatch = apothecaryBlockService.ResolvePendingApothecary(apothecaryBlockMatch, loadedLeague.Teams[0], useApothecary: true);
var savedAttacker = apothecaryUsedMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(apothecaryUsedMatch.HomeApothecariesRemaining == 0, "apothecary should be spent after the user chooses to use it");
Assert(savedAttacker.State == PlayerPitchState.Casualty && savedAttacker.Casualty?.Result == CasualtyResult.BadlyHurt, "apothecary should keep the better casualty roll");

var regenerationTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "regeneration"] } : player)
        .ToArray()
};
var regenerationService = new MatchService(new FixedDiceRoller(d6: [6, 6, 6, 5, 5, 4], d16: [16]));
var regenerationMatch = regenerationService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, regenerationTeam, awayPlayerToPlace.Id);
regenerationMatch = ConfirmBlockDie(regenerationService, regenerationMatch, ruleset, loadedLeague.Teams[0], regenerationTeam);
regenerationMatch = regenerationService.ChoosePushSquare(regenerationMatch, ruleset, loadedLeague.Teams[0], regenerationTeam, new PitchSquare(3, 1));
Assert(regenerationMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Reserve, "Regeneration should recover a casualty on 4+");

var decayTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "decay"] } : player)
        .ToArray()
};
var decayService = new MatchService(new FixedDiceRoller(d6: [6, 6, 6, 5, 5], d16: [1, 16]));
var decayMatch = decayService.BlockPlayer(blockReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, decayTeam, awayPlayerToPlace.Id);
decayMatch = ConfirmBlockDie(decayService, decayMatch, ruleset, loadedLeague.Teams[0], decayTeam);
decayMatch = decayService.ChoosePushSquare(decayMatch, ruleset, loadedLeague.Teams[0], decayTeam, new PitchSquare(3, 1));
Assert(decayMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Dead, "Decay should roll two casualty results and keep the worse outcome");

var plagueTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "plague-ridden"] } : player)
        .ToArray()
};
var postMatchCasualtyLeague = league with { Teams = [plagueTeam, awayLeague.Teams[0]] };
var postMatchCasualties = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with
            {
                Square = null,
                State = PlayerPitchState.Casualty,
                Casualty = new CasualtyRoll { Roll = 13, Result = CasualtyResult.LastingInjury }
            }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with
                {
                    Square = null,
                    State = PlayerPitchState.Dead,
                    Casualty = new CasualtyRoll { Roll = 16, Result = CasualtyResult.Dead }
                }
                : placement)
        .ToArray()
};
var updatedCasualtyLeague = leagueService.ApplyMatchCasualties(postMatchCasualtyLeague, ruleset, postMatchCasualties);
var injuredRosterPlayer = updatedCasualtyLeague.Teams.Single(team => team.Id == plagueTeam.Id).Players.Single(player => player.Id == playerToPlace.Id);
var deadRosterPlayer = updatedCasualtyLeague.Teams.Single(team => team.Id == awayLeague.Teams[0].Id).Players.Single(player => player.Id == awayPlayerToPlace.Id);
Assert(injuredRosterPlayer.Status == PlayerStatus.MissNextGame, "lasting injuries should mark players as missing the next game");
Assert(injuredRosterPlayer.Stats.Movement == playerToPlace.Stats.Movement - 1, "lasting injuries should apply a stat modifier");
Assert(deadRosterPlayer.Status == PlayerStatus.Dead, "dead casualty results should be applied back to league rosters");
Assert(updatedCasualtyLeague.Teams.Single(team => team.Id == plagueTeam.Id).Players.Count == plagueTeam.Players.Count + 1, "Plague Ridden should add a replacement player after an opposing death when roster space exists");

var blitzReadyMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(0, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(3, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var blitzService = new MatchService(new FixedDiceRoller(d6: [6, 1, 1]));
var blitzMatch = blitzService.BlitzPlayer(blitzReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(2, 1), awayLeague.Teams[0], awayPlayerToPlace.Id);
blitzMatch = ConfirmBlockDie(blitzService, blitzMatch, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
var blitzActivation = blitzMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id);

Assert(blitzActivation.Action == PlayerTurnAction.Blitz, "blitz should record a blitz activation");
Assert(blitzMatch.PendingPush?.KnockDefenderDown == true, "blitz should ask for a push square before block knockdown");
blitzMatch = blitzService.ChoosePushSquare(blitzMatch, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(4, 1));

Assert(blitzMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(2, 1), "blitz should move the attacker");
Assert(blitzMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "blitz should resolve the block");

var declaredBlitzService = new MatchService(new FixedDiceRoller(d6: [6, 1, 1]));
var declaredBlitz = declaredBlitzService.DeclarePlayerAction(blitzReadyMatch, loadedLeague.Teams[0], playerToPlace.Id, PlayerTurnAction.Blitz);
var declaredBlitzMoved = declaredBlitzService.MovePlayerAsBlitz(declaredBlitz, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(2, 1), awayLeague.Teams[0]);
var declaredBlitzResolved = declaredBlitzService.BlitzPlayer(declaredBlitzMoved, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(2, 1), awayLeague.Teams[0], awayPlayerToPlace.Id);
declaredBlitzResolved = ConfirmBlockDie(declaredBlitzService, declaredBlitzResolved, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);

Assert(declaredBlitzMoved.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Blitz, "declared blitz movement should keep the blitz activation");
Assert(declaredBlitzResolved.PendingPush?.KnockDefenderDown == true, "declared blitz should allow a later adjacent block from the current square");

var failedMoveBlitzMatch = blitzReadyMatch with
{
    Placements = blitzReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayPlayerToPlace.Id
            ? placement with { Square = new PitchSquare(10, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var failedMoveBlitzService = new MatchService(new FixedDiceRoller(d6: [1], d8: [5]));
var failedMoveBlitzResult = failedMoveBlitzService.BlitzPlayer(failedMoveBlitzMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(9, 1), awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(failedMoveBlitzResult.PendingReroll?.Kind == PendingRerollKind.GoForIt, "failed blitz movement should pause before resolving the failed movement roll");
Assert(failedMoveBlitzResult.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Blitz, "failed blitz movement should still spend the blitz activation");
Assert(failedMoveBlitzResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Standing, "failed blitz movement should not resolve the block");

smoke.StartSection("Lazy action declaration");
var homeTeamId = loadedLeague.Teams[0].Id;

// Lazy blitz: take plain Move steps with no declaration, then block from the adjacent square. The block
// upgrades the provisional Move to a Blitz, carrying the squares already spent and spending the team blitz.
var lazyBlitzService = new MatchService(new FixedDiceRoller(d6: [6, 1, 1]));
var lazyBlitzMoved = lazyBlitzService.MovePlayer(blitzReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new PitchSquare(2, 1), awayLeague.Teams[0]);
var lazyMovedActivation = lazyBlitzMoved.Activations.Single(activation => activation.PlayerId == playerToPlace.Id);
Assert(lazyMovedActivation.Action == PlayerTurnAction.Move, "an undeclared move should record a provisional Move activation");
Assert(lazyMovedActivation.MovementSquaresUsed == 2, "the provisional move should record the squares it spent");
Assert(!MatchQueries.HasUsedBlitz(lazyBlitzMoved, homeTeamId), "a plain move must not consume the team blitz");
var lazyBlitzed = lazyBlitzService.BlitzPlayer(lazyBlitzMoved, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new PitchSquare(2, 1), awayLeague.Teams[0], awayPlayerToPlace.Id);
var lazyBlitzActivation = lazyBlitzed.Activations.Single(activation => activation.PlayerId == playerToPlace.Id);
Assert(lazyBlitzActivation.Action == PlayerTurnAction.Blitz, "blocking after a plain move should upgrade the activation to Blitz");
Assert(lazyBlitzActivation.MovementSquaresUsed == 2, "the upgraded blitz should preserve the squares already moved");
Assert(MatchQueries.HasUsedBlitz(lazyBlitzed, homeTeamId), "the upgraded blitz should consume the team blitz");

// Gotcha: a lazy blitz that still has movement to a destination must carry the provisional move's squares
// into the blitz move, not reset them to zero (which would let the blitzer over-move).
var lazyBlitzMoveService = new MatchService(new FixedDiceRoller(d6: [6, 1, 1]));
var lazyBlitzStep = lazyBlitzMoveService.MovePlayer(blitzReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new PitchSquare(1, 1), awayLeague.Teams[0]);
var lazyBlitzToTarget = lazyBlitzMoveService.BlitzPlayer(lazyBlitzStep, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new PitchSquare(2, 1), awayLeague.Teams[0], awayPlayerToPlace.Id);
var lazyBlitzToTargetActivation = lazyBlitzToTarget.Activations.Single(activation => activation.PlayerId == playerToPlace.Id);
Assert(lazyBlitzToTargetActivation.Action == PlayerTurnAction.Blitz, "moving then blitzing to a destination should upgrade to Blitz");
Assert(lazyBlitzToTargetActivation.MovementSquaresUsed == 2, "the upgraded blitz must carry the squares spent during the provisional move");

// A consumed lazy blitz still reserves the team's once-per-turn blitz: a second player cannot blitz.
var lazyBlitzResolved = ConfirmBlockDie(lazyBlitzService, lazyBlitzed, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
if (lazyBlitzResolved.PendingPush is not null)
{
    lazyBlitzResolved = lazyBlitzService.ChoosePushSquare(lazyBlitzResolved, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(4, 1));
}
AssertThrows(
    () => lazyBlitzService.DeclarePlayerAction(lazyBlitzResolved, loadedLeague.Teams[0], handOffReceiver.Id, PlayerTurnAction.Blitz),
    "a lazy blitz should consume the team blitz so no second blitz can be taken");

// Lazy hand-off: move the carrier without declaring, then hand off to an adjacent team-mate.
var lazyHandOffService = new MatchService(new FixedDiceRoller(d6: [4]));
var lazyHandOffMoved = lazyHandOffService.MovePlayer(handOffReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new PitchSquare(1, 0), awayLeague.Teams[0]);
Assert(lazyHandOffMoved.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Move, "moving the carrier without declaring should stay a provisional Move");
Assert(!MatchQueries.HasUsedHandOff(lazyHandOffMoved, homeTeamId), "a provisional move must not consume the hand-off");
var lazyHandOffDone = lazyHandOffService.HandOffBall(lazyHandOffMoved, ruleset, loadedLeague.Teams[0], playerToPlace.Id, handOffReceiver.Id);
Assert(lazyHandOffDone.Ball.CarrierPlayerId == handOffReceiver.Id, "handing off after a plain move should transfer the ball");
Assert(lazyHandOffDone.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.HandOff, "handing off should upgrade the provisional Move to Hand-off");
Assert(MatchQueries.HasUsedHandOff(lazyHandOffDone, homeTeamId), "the upgraded hand-off should consume the team hand-off");

// Lazy pass: move the passer without declaring, then throw.
var lazyPassService = new MatchService(new FixedDiceRoller(d6: [6, 6]));
var lazyPassMoved = lazyPassService.MovePlayer(passReadyMatch, ruleset, loadedLeague.Teams[0], passerPlayer.Id, new PitchSquare(1, 0), awayLeague.Teams[0]);
Assert(!MatchQueries.HasUsedPass(lazyPassMoved, homeTeamId), "a provisional move must not consume the pass");
var lazyPassDone = lazyPassService.PassBall(lazyPassMoved, ruleset, loadedLeague.Teams[0], passerPlayer.Id, passReceiver.Id);
Assert(lazyPassDone.Ball.CarrierPlayerId == passReceiver.Id, "passing after a plain move should transfer the ball");
Assert(lazyPassDone.Activations.Single(activation => activation.PlayerId == passerPlayer.Id).Action == PlayerTurnAction.Pass, "throwing should upgrade the provisional Move to Pass");
Assert(MatchQueries.HasUsedPass(lazyPassDone, homeTeamId), "the upgraded pass should consume the team pass");

// Lazy foul: move adjacent to a prone victim without declaring, then foul.
var lazyFoulBase = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(0, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Prone }
                : placement)
        .ToArray()
};
var lazyFoulService = new MatchService(new FixedDiceRoller(d6: [2, 3]));
var lazyFoulMoved = lazyFoulService.MovePlayer(lazyFoulBase, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new PitchSquare(1, 1), awayLeague.Teams[0]);
Assert(!MatchQueries.HasUsedFoul(lazyFoulMoved, homeTeamId), "a provisional move must not consume the foul");
var lazyFoulDone = lazyFoulService.FoulPlayer(lazyFoulMoved, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
Assert(lazyFoulDone.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Foul, "fouling after a plain move should upgrade the provisional Move to Foul");
Assert(MatchQueries.HasUsedFoul(lazyFoulDone, homeTeamId), "the upgraded foul should consume the team foul");

var assistingPlayer = loadedLeague.Teams[0].Players[1];
var assistedBlockMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == assistingPlayer.Id
                ? placement with { Square = new PitchSquare(2, 2), State = PlayerPitchState.Standing }
                : placement.PlayerId == awayPlayerToPlace.Id
                    ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                    : placement)
        .ToArray()
};
var assistedBlockService = new MatchService(new FixedDiceRoller(d6: [1, 6, 1, 1]));
var assistedPendingBlock = assistedBlockService.BlockPlayer(assistedBlockMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(assistedPendingBlock.PendingBlock?.Rolls.SequenceEqual([1, 6]) == true, "multi-die assisted block should wait for player choice");

var assistedBlockResult = assistedBlockService.ChooseBlockDie(assistedPendingBlock, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], roll: 6);

Assert(assistedBlockResult.PendingBlock is null, "choosing a block die should clear pending block choice");
Assert(assistedBlockResult.PendingPush is not null, "chosen favorable block die should ask for a push square");
assistedBlockResult = assistedBlockService.ChoosePushSquare(assistedBlockResult, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));
Assert(assistedBlockResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "chosen favorable block die should knock defender down");

var blockRerollPendingService = new MatchService(new FixedDiceRoller(d6: [1, 1, 6, 6]));
var blockRerollPending = blockRerollPendingService.BlockPlayer(assistedBlockMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
Assert(blockRerollPending.PendingBlock?.Rolls.SequenceEqual([1, 1]) == true, "multi-die block should present the rolled dice for a choice");
var blockRerolledChoice = blockRerollPendingService.RerollPendingBlock(blockRerollPending, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
Assert(blockRerolledChoice.PendingBlock?.Rolls.SequenceEqual([6, 6]) == true, "rerolling a pending block should reroll all dice and re-present the choice");
Assert(blockRerolledChoice.HomeRerollsRemaining == loadedLeague.Teams[0].Rerolls - 1, "rerolling a pending block should spend a team reroll");

var guardTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == assistingPlayer.Id ? player with { Skills = [.. player.Skills, "guard"] } : player)
        .ToArray()
};
var guardAssistMatch = assistedBlockMatch with
{
    Placements = assistedBlockMatch.Placements
        .Select(placement => placement.PlayerId == awayLeague.Teams[0].Players[1].Id
            ? placement with { Square = new PitchSquare(3, 2), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var guardAssistService = new MatchService(new FixedDiceRoller(d6: [1, 6]));
var guardPendingBlock = guardAssistService.BlockPlayer(guardAssistMatch, ruleset, guardTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(guardPendingBlock.PendingBlock?.Rolls.SequenceEqual([1, 6]) == true, "guard should allow a marked player to assist a block");

var weakBlockMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == loadedLeague.Teams[0].Players[7].Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayLeague.Teams[0].Players[3].Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var weakBlockService = new MatchService(new FixedDiceRoller(d6: [6, 1, 6, 6, 6]));
var weakPendingBlock = weakBlockService.BlockPlayer(weakBlockMatch, ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0].Players[7].Id, awayLeague.Teams[0], awayLeague.Teams[0].Players[3].Id);

Assert(weakPendingBlock.PendingBlock?.Rolls.SequenceEqual([6, 1]) == true, "unfavorable multi-die block should still wait for player choice");

var weakBlockResult = weakBlockService.ChooseBlockDie(weakPendingBlock, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], roll: 6);

weakBlockResult = weakBlockService.ChoosePushSquare(weakBlockResult, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));
Assert(weakBlockResult.Placements.Single(placement => placement.PlayerId == awayLeague.Teams[0].Players[3].Id).State == PlayerPitchState.Casualty, "chosen high block die with injury roll of 10+ should injure the defender");
Assert(weakBlockResult.Placements.Single(placement => placement.PlayerId == awayLeague.Teams[0].Players[3].Id).Casualty?.Result == CasualtyResult.BadlyHurt, "casualty details should be stored on injured players");

var safePairTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "safe-pair-of-hands"] } : player)
        .ToArray()
};
var safePairService = new MatchService(new FixedDiceRoller(d6: [6, 1, 1]));
var safePairPending = safePairService.BlockPlayer(blockReadyMatch with { Ball = new BallState { CarrierPlayerId = awayPlayerToPlace.Id } }, ruleset, loadedLeague.Teams[0], playerToPlace.Id, safePairTeam, awayPlayerToPlace.Id);
safePairPending = ConfirmBlockDie(safePairService, safePairPending, ruleset, loadedLeague.Teams[0], safePairTeam);
safePairPending = safePairService.ChoosePushSquare(safePairPending, ruleset, loadedLeague.Teams[0], safePairTeam, new PitchSquare(3, 1));
Assert(safePairPending.PendingBallPlacement?.Reason == "Safe Pair of Hands", "Safe Pair of Hands should create a legal ball-placement choice when the carrier is knocked down");
var safePairPlaced = safePairService.ChooseBallPlacement(safePairPending, safePairTeam, safePairPending.PendingBallPlacement!.LegalSquares[0]);
Assert(safePairPlaced.Ball.Square == safePairPending.PendingBallPlacement.LegalSquares[0], "Safe Pair of Hands should place the ball on the chosen legal square");

var multipleBlockTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "multiple-block"] } : player)
        .ToArray()
};
var multipleBlockFirstDefender = awayPlayerToPlace;
var multipleBlockSecondDefender = awayLeague.Teams[0].Players[1];
var multipleBlockMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == multipleBlockFirstDefender.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement.PlayerId == multipleBlockSecondDefender.Id
                    ? placement with { Square = new PitchSquare(1, 2), State = PlayerPitchState.Standing }
                    : placement with { Square = null, State = PlayerPitchState.Reserve })
        .ToArray()
};
var multipleBlockService = new MatchService(new FixedDiceRoller(d6: [6, 6, 6, 6]));
var multipleBlockPending = multipleBlockService.MultipleBlockPlayer(multipleBlockMatch, ruleset, multipleBlockTeam, playerToPlace.Id, awayLeague.Teams[0], multipleBlockFirstDefender.Id, multipleBlockSecondDefender.Id);

Assert(multipleBlockPending.PendingBlock?.AttackerStrength == 3 && multipleBlockPending.PendingBlock.DefenderStrength == 6, "Multiple Block should give the first defender +2 strength while preserving normal defensive assists");
Assert(multipleBlockPending.PendingBlock?.PreventFollowUp == true, "Multiple Block pending block should suppress follow-up");
var multipleBlockFirstResolved = multipleBlockService.ChooseBlockDie(multipleBlockPending, ruleset, multipleBlockTeam, awayLeague.Teams[0], roll: 6);
multipleBlockFirstResolved = multipleBlockService.ChoosePushSquare(multipleBlockFirstResolved, ruleset, multipleBlockTeam, awayLeague.Teams[0], new PitchSquare(3, 1));
Assert(multipleBlockFirstResolved.PendingFollowUp is null, "Multiple Block should not offer a follow-up choice after the first block");
Assert(multipleBlockFirstResolved.PendingMultipleBlock is not null, "Multiple Block should keep the second block pending after resolving the first block");
Assert(multipleBlockFirstResolved.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Completed == false, "Multiple Block should not end the blocker's activation while a second block is still pending");
var multipleBlockSecondPending = multipleBlockService.ContinueMultipleBlock(multipleBlockFirstResolved, ruleset, multipleBlockTeam, awayLeague.Teams[0]);
Assert(multipleBlockSecondPending.PendingBlock?.DefenderPlayerId == multipleBlockSecondDefender.Id, "Multiple Block continuation should target the second defender");
Assert(multipleBlockSecondPending.PendingBlock?.DefenderStrength == 5, "Multiple Block should give the second defender +2 strength after the first defender is no longer assisting");

var pileDriverTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "pile-driver"] } : player)
        .ToArray()
};
var pileDriverService = new MatchService(new FixedDiceRoller(d6: [6, 6, 6, 3, 4, 5, 6, 3, 4]));
var pileDriverBlock = pileDriverService.BlockPlayer(blockReadyMatch, ruleset, pileDriverTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
pileDriverBlock = ConfirmBlockDie(pileDriverService, pileDriverBlock, ruleset, pileDriverTeam, awayLeague.Teams[0]);
pileDriverBlock = pileDriverService.ChoosePushSquare(pileDriverBlock, ruleset, pileDriverTeam, awayLeague.Teams[0], new PitchSquare(3, 1));
pileDriverBlock = pileDriverService.ResolvePendingFollowUp(pileDriverBlock, pileDriverTeam, awayLeague.Teams[0], useFollowUp: true);
var pileDriverResult = pileDriverService.PileDriverPlayer(pileDriverBlock, ruleset, pileDriverTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
Assert(pileDriverResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Prone, "Pile Driver should place the blocking player prone");
Assert(pileDriverResult.Log.Any(entry => entry.Message.Contains("uses Pile Driver")), "Pile Driver should log the follow-up foul");

var foulReadyMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Prone }
                : placement)
        .ToArray()
};
var foulService = new MatchService(new FixedDiceRoller(d6: [5, 6, 3, 4]));
var foulMatch = foulService.FoulPlayer(foulReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(foulMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Foul, "foul should activate the fouler");
Assert(foulMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Stunned, "foul armor break should resolve injury against the victim");
Assert(foulMatch.Phase == MatchPhase.OffensivePlayerTurn, "foul without doubles should not cause a turnover");

var dirtyPlayerTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "dirty-player"] } : player)
        .ToArray()
};
var dirtyPlayerService = new MatchService(new FixedDiceRoller(d6: [5, 5, 3, 4]));
var dirtyPlayerFoulMatch = dirtyPlayerService.FoulPlayer(foulReadyMatch, ruleset, dirtyPlayerTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(dirtyPlayerFoulMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Stunned, "dirty player should be able to turn a tied foul armor roll into an armor break");

var thickSkullTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "thick-skull"] } : player)
        .ToArray()
};
var thickSkullFoulService = new MatchService(new FixedDiceRoller(d6: [5, 6, 3, 5]));
var thickSkullFoulMatch = thickSkullFoulService.FoulPlayer(foulReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, thickSkullTeam, awayPlayerToPlace.Id);

Assert(thickSkullFoulMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Stunned, "thick skull should turn an injury roll of 8 from KO into stunned");

smoke.StartSection("Fouls, rushes, dodges, and movement skills");
AssertThrows(
    () => foulService.FoulPlayer(foulMatch, ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0].Players[1].Id, awayLeague.Teams[0], awayPlayerToPlace.Id),
    "foul should be limited to once per team turn");

var sentOffFoulService = new MatchService(new FixedDiceRoller(d6: [6, 6, 4, 5]));
var sentOffFoulMatch = sentOffFoulService.FoulPlayer(foulReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);

Assert(sentOffFoulMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.SentOff, "doubles on a foul should send off the fouler");
Assert(sentOffFoulMatch.Phase == MatchPhase.DefensiveTurn, "send-off on a foul should cause a turnover");

var bribeFoulService = new MatchService(new FixedDiceRoller(d6: [6, 6, 4, 5, 3]));
var bribeFoulPending = bribeFoulService.FoulPlayer(foulReadyMatch with { HomeBribesRemaining = 1 }, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
Assert(bribeFoulPending.PendingSendOff?.Reason == "foul", "foul send-off with an available bribe should create a pending bribe choice");
var bribeFoulResolved = bribeFoulService.ResolvePendingSendOff(bribeFoulPending, ruleset, loadedLeague.Teams[0], useBribe: true);
Assert(bribeFoulResolved.HomeBribesRemaining == 0, "foul bribe choice should spend the bribe");
Assert(bribeFoulResolved.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Standing, "successful foul bribe should keep the fouler on the pitch");
Assert(bribeFoulResolved.Phase == MatchPhase.OffensivePlayerTurn, "successful foul bribe should avoid the send-off turnover");

var declinedBribeFoulService = new MatchService(new FixedDiceRoller(d6: [6, 6, 4, 5]));
var declinedBribeFoulPending = declinedBribeFoulService.FoulPlayer(foulReadyMatch with { HomeBribesRemaining = 1 }, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
var declinedBribeFoul = declinedBribeFoulService.ResolvePendingSendOff(declinedBribeFoulPending, ruleset, loadedLeague.Teams[0], useBribe: false);
Assert(declinedBribeFoul.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.SentOff, "declining a foul bribe should send off the fouler");
Assert(declinedBribeFoul.Phase == MatchPhase.DefensiveTurn, "declining a foul bribe should keep the send-off turnover");

var sneakyGitTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "sneaky-git"] } : player)
        .ToArray()
};
var sneakyGitService = new MatchService(new FixedDiceRoller(d6: [5, 5]));
var sneakyGitFoulMatch = sneakyGitService.FoulPlayer(foulReadyMatch, ruleset, sneakyGitTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
var sneakyGitMoveMatch = sneakyGitService.MovePlayer(sneakyGitFoulMatch, ruleset, sneakyGitTeam, playerToPlace.Id, new PitchSquare(1, 2));
var sneakyGitActivation = sneakyGitMoveMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id);
Assert(sneakyGitMoveMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 2), "Sneaky Git should allow the fouler to keep moving after a non-send-off foul");
Assert(sneakyGitActivation.Action == PlayerTurnAction.Foul && !sneakyGitActivation.MayMoveAfterFoul, "Sneaky Git movement should preserve the spent foul action and consume the continuation");

var goForItService = new MatchService(new FixedDiceRoller(d6: [2]));
var goForItMatch = goForItService.MovePlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(7, 0));
var goForItActivation = goForItMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id);

Assert(goForItActivation.GoForItsUsed == 1, "movement past MA should spend go-for-its");
Assert(goForItMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(7, 0), "successful go-for-it should move the player");

var blizzardGoForItService = new MatchService(new FixedDiceRoller(d6: [2]));
var blizzardGoForItMatch = blizzardGoForItService.MovePlayer(
    offensiveTurnMatch with { Weather = WeatherCondition.Blizzard },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(7, 0));

Assert(blizzardGoForItMatch.PendingReroll?.Kind == PendingRerollKind.GoForIt, "blizzard should make a normal 2+ go-for-it need 3+");
Assert(blizzardGoForItMatch.PendingReroll?.Target == 3, "blizzard go-for-it target should be 3+");

var proTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "pro"] } : player)
        .ToArray()
};
var proRerollService = new MatchService(new FixedDiceRoller(d6: [1, 3, 2]));
var proPendingMatch = proRerollService.MovePlayer(offensiveTurnMatch with { HomeRerollsRemaining = 0 }, ruleset, proTeam, playerToPlace.Id, new(7, 0));
Assert(proPendingMatch.PendingReroll?.SkillRerollIds.Contains("pro") == true, "Pro should be offered as a conditional skill reroll when no team reroll is available");
var proResolvedMatch = proRerollService.ResolvePendingReroll(proPendingMatch, ruleset, proTeam, useTeamReroll: false, skillId: "pro");
Assert(proResolvedMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(7, 0), "successful Pro should allow the failed roll to be rerolled");

var proneMoveReadyMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 0), State = PlayerPitchState.Prone }
            : placement)
        .ToArray()
};
var standOnlyMatch = matchService.MovePlayer(proneMoveReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(1, 0));
var stoodPlayer = standOnlyMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(stoodPlayer.State == PlayerPitchState.Standing, "move action should allow a prone player to stand up");
Assert(stoodPlayer.Square == new PitchSquare(1, 0), "standing up without moving should keep the player in place");
Assert(standOnlyMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Move, "standing up should spend a move activation");
Assert(standOnlyMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).MovementSquaresUsed == 3, "standing up should record the movement allowance it spends");

var continuedAfterStandMatch = matchService.MovePlayer(standOnlyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(4, 0));
var continuedAfterStandActivation = continuedAfterStandMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id);
Assert(continuedAfterStandMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(4, 0), "player should be able to continue moving after standing up");
Assert(continuedAfterStandActivation.MovementSquaresUsed == 6, "continuing after standing should count the stand-up cost plus moved squares");
Assert(continuedAfterStandActivation.GoForItsUsed == 0, "continuing within remaining movement after standing should not spend go-for-its");

var proneMoveService = new MatchService(new FixedDiceRoller(d6: [2]));
var stoodAndMovedMatch = proneMoveService.MovePlayer(proneMoveReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(5, 0));
var stoodAndMovedActivation = stoodAndMovedMatch.Activations.Single(activation => activation.PlayerId == playerToPlace.Id);

Assert(stoodAndMovedMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(5, 0), "standing up should still allow movement with reduced allowance");
Assert(stoodAndMovedActivation.GoForItsUsed == 1, "standing up should reduce movement allowance before go-for-its");

var proneBlitzReadyMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Prone }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var proneBlitzService = new MatchService(new FixedDiceRoller(d6: [6, 1, 1]));
var proneBlitzMatch = proneBlitzService.BlitzPlayer(proneBlitzReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(1, 1), awayLeague.Teams[0], awayPlayerToPlace.Id);
proneBlitzMatch = ConfirmBlockDie(proneBlitzService, proneBlitzMatch, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);

Assert(proneBlitzMatch.PendingPush?.KnockDefenderDown == true, "prone adjacent blitz should stand up and resolve the block");
Assert(proneBlitzMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Standing, "prone blitz should stand the attacker up");

var jumpUpBlockTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "jump-up"] } : player)
        .ToArray()
};
var jumpUpBlockMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Prone }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var jumpUpBlockService = new MatchService(new FixedDiceRoller(d6: [4, 6]));
var jumpUpBlockResult = jumpUpBlockService.BlockPlayer(jumpUpBlockMatch, ruleset, jumpUpBlockTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
jumpUpBlockResult = ConfirmBlockDie(jumpUpBlockService, jumpUpBlockResult, ruleset, jumpUpBlockTeam, awayLeague.Teams[0]);
Assert(jumpUpBlockResult.PendingPush?.KnockDefenderDown == true, "Jump Up should allow a prone player to block after passing the agility test");
Assert(jumpUpBlockResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Standing, "successful Jump Up block should stand the blocker");

// A failed Jump Up agility roll may be rerolled before the block proceeds.
var jumpUpRerollService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6]));
var jumpUpRerollPending = jumpUpRerollService.BlockPlayer(jumpUpBlockMatch, ruleset, jumpUpBlockTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
Assert(jumpUpRerollPending.PendingReroll?.Kind == PendingRerollKind.JumpUp, "a failed Jump Up roll should offer a reroll");
var jumpUpRerolled = jumpUpRerollService.ResolvePendingReroll(jumpUpRerollPending, ruleset, jumpUpBlockTeam, useTeamReroll: true, opposingTeam: awayLeague.Teams[0]);
Assert(jumpUpRerolled.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Standing, "a successful Jump Up reroll should stand the blocker");
Assert(jumpUpRerolled.PendingBlock is not null, "a successful Jump Up reroll should proceed to the block dice");

var failedGoForItService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6], d8: [5]));
var failedGoForItMatch = failedGoForItService.MovePlayer(
    offensiveTurnMatch with { Ball = new BallState { CarrierPlayerId = playerToPlace.Id } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(7, 0));
Assert(failedGoForItMatch.PendingReroll?.Kind == PendingRerollKind.GoForIt, "failed go-for-it should offer a pending reroll before resolving failure");
failedGoForItMatch = failedGoForItService.ResolvePendingReroll(failedGoForItMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false);
var failedGoForItPlayer = failedGoForItMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(failedGoForItMatch.Phase == MatchPhase.DefensiveTurn, "failed offensive go-for-it should cause a turnover to defensive turn");
Assert(failedGoForItMatch.ActiveTeamId == awayLeague.Teams[0].Id, "failed offensive go-for-it should activate defense");
Assert(failedGoForItMatch.PendingBlock is null && failedGoForItMatch.PendingPush is null && failedGoForItMatch.PendingInterception is null && failedGoForItMatch.PendingReroll is null, "turnover cleanup should clear pending choices");
Assert(failedGoForItPlayer.State == PlayerPitchState.Casualty, "failed go-for-it injury roll of 10+ should injure the player");
Assert(failedGoForItMatch.Ball.CarrierPlayerId is null, "failed ball carrier go-for-it should drop the ball");
Assert(failedGoForItMatch.Ball.Square == new PitchSquare(8, 0), "failed ball carrier go-for-it should scatter the ball");

// BB2020 permits multiple team rerolls in one team turn, provided the team has rerolls remaining
// and no individual dice roll is rerolled more than once.
var usedRerollGoForItService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6], d8: [5]));
var usedRerollGoForItMatch = usedRerollGoForItService.MovePlayer(
    offensiveTurnMatch with
    {
        HomeRerollsRemaining = 2,
        TeamRerollUses =
        [
            new TeamRerollUse
            {
                TeamId = loadedLeague.Teams[0].Id,
                Half = offensiveTurnMatch.Half,
                Turn = offensiveTurnMatch.Turn
            }
        ]
    },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(7, 0));
Assert(usedRerollGoForItMatch.PendingReroll is { Kind: PendingRerollKind.GoForIt, TeamRerollAvailable: true }, "BB2020 should offer another team reroll after earlier same-turn usage when rerolls remain");
usedRerollGoForItMatch = usedRerollGoForItService.ResolvePendingReroll(usedRerollGoForItMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: true);
Assert(usedRerollGoForItMatch.PendingReroll is null, "a successful same-turn team reroll should resolve the failed go-for-it");
Assert(usedRerollGoForItMatch.HomeRerollsRemaining == 1, "using another team reroll in the same turn should spend one remaining reroll");
Assert(usedRerollGoForItMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(7, 0), "the successful rerolled go-for-it should complete movement");

// A loose ball that scatters onto a Standing opponent must force a catch attempt rather than
// silently resting on top of the player (which previously hid the catcher under the ball).
var bounceCatcher = awayLeague.Teams[0].Players[1];
var bounceCatchAwayTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == bounceCatcher.Id ? player with { Stats = player.Stats with { Agility = 2 } } : player)
        .ToArray()
};
var bounceCatchMatch = offensiveTurnMatch with
{
    Ball = new BallState { CarrierPlayerId = playerToPlace.Id },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == bounceCatcher.Id
            ? placement with { Square = new PitchSquare(8, 0), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var bounceCatchService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6, 6], d8: [5]));
bounceCatchService.RegisterTeams(loadedLeague.Teams[0], bounceCatchAwayTeam);
var bounceCatchPending = bounceCatchService.MovePlayer(
    bounceCatchMatch,
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(7, 0));
var bounceCatchResult = bounceCatchService.ResolvePendingReroll(bounceCatchPending, ruleset, loadedLeague.Teams[0], useTeamReroll: false);

Assert(bounceCatchResult.Ball.CarrierPlayerId == bounceCatcher.Id, "a ball scattering onto a standing opponent should trigger a catch attempt instead of resting on them");
Assert(bounceCatchResult.Ball.Square is null, "a caught scattered ball must not also be left resting on a square");

// A missed catch keeps the ball bouncing until it reaches a square no standing player holds,
// so it never comes to rest sharing a square with a player.
var bounceMissService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6, 1], d8: [5, 5]));
bounceMissService.RegisterTeams(loadedLeague.Teams[0], bounceCatchAwayTeam);
var bounceMissPending = bounceMissService.MovePlayer(
    bounceCatchMatch,
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(7, 0));
var bounceMissResult = bounceMissService.ResolvePendingReroll(bounceMissPending, ruleset, loadedLeague.Teams[0], useTeamReroll: false);

Assert(bounceMissResult.Ball.CarrierPlayerId is null, "a missed catch on a scattered ball should leave the ball loose, not carried");
Assert(bounceMissResult.Ball.Square == new PitchSquare(9, 0), "a missed catch should bounce the ball on past the player to the next empty square");

// A prone/stunned player cannot catch, and the ball cannot rest on them either: it bounces on.
var proneBounceMatch = offensiveTurnMatch with
{
    Ball = new BallState { CarrierPlayerId = playerToPlace.Id },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == bounceCatcher.Id
            ? placement with { Square = new PitchSquare(8, 0), State = PlayerPitchState.Prone }
            : placement)
        .ToArray()
};
var proneBounceService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6], d8: [5, 5]));
proneBounceService.RegisterTeams(loadedLeague.Teams[0], awayLeague.Teams[0]);
var proneBouncePending = proneBounceService.MovePlayer(
    proneBounceMatch,
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(7, 0));
var proneBounceResult = proneBounceService.ResolvePendingReroll(proneBouncePending, ruleset, loadedLeague.Teams[0], useTeamReroll: false);

Assert(proneBounceResult.Ball.CarrierPlayerId is null, "a prone player cannot catch a loose ball");
Assert(proneBounceResult.Ball.Square == new PitchSquare(9, 0), "a loose ball scattering onto a prone player should bounce on to the next empty square, not rest on them");

var dodgeReadyMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var dodgeService = new MatchService(new FixedDiceRoller(d6: [3]));
var dodgedMatch = dodgeService.MovePlayer(dodgeReadyMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(1, 2));

Assert(dodgedMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 2), "successful dodge should move the player");
Assert(dodgedMatch.Phase == MatchPhase.OffensivePlayerTurn, "successful dodge should not cause a turnover");

var breakTackleTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "break-tackle"] } : player)
        .ToArray()
};
var breakTackleMatch = dodgeReadyMatch with
{
    Placements = dodgeReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayLeague.Teams[0].Players[1].Id
            ? placement with { Square = new PitchSquare(1, 3), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var breakTackleService = new MatchService(new FixedDiceRoller(d6: [3]));
var breakTackleResult = breakTackleService.MovePlayer(breakTackleMatch, ruleset, breakTackleTeam, playerToPlace.Id, new(1, 2));
Assert(breakTackleResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 2), "Break Tackle should improve one dodge based on strength");

var dodgeSkillMatch = offensiveTurnMatch with
{
    HomeRerollsRemaining = 0,
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == passReceiver.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var dodgeSkillService = new MatchService(new FixedDiceRoller(d6: [1], d8: [5]));
var dodgeSkillPending = dodgeSkillService.MovePlayer(dodgeSkillMatch, ruleset, loadedLeague.Teams[0], passReceiver.Id, new(1, 2), awayLeague.Teams[0]);

Assert(dodgeSkillPending.PendingReroll?.SkillRerollIds.Contains("dodge") == true, "dodge skill should offer a ruleset-backed dodge reroll");

var tackleTeam = awayLeague.Teams[0] with
{
    Players = awayLeague.Teams[0].Players
        .Select(player => player.Id == awayPlayerToPlace.Id ? player with { Skills = [.. player.Skills, "tackle"] } : player)
        .ToArray()
};
var tackleDodgeService = new MatchService(new FixedDiceRoller(d6: [1, 1, 1], d8: [5]));
var tackleDodgeResult = tackleDodgeService.MovePlayer(dodgeSkillMatch, ruleset, loadedLeague.Teams[0], passReceiver.Id, new(1, 2), tackleTeam);

Assert(tackleDodgeResult.PendingReroll is null, "tackle should cancel the dodge skill reroll when dodging away");
Assert(tackleDodgeResult.Phase == MatchPhase.DefensiveTurn, "failed dodge without an available reroll should resolve immediately");

var twoTackleZoneDodgeMatch = dodgeReadyMatch with
{
    Placements = dodgeReadyMatch.Placements
        .Select(placement => placement.PlayerId == awayLeague.Teams[0].Players[1].Id
            ? placement with { Square = new PitchSquare(1, 3), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var markedDodgeService = new MatchService(new FixedDiceRoller(d6: [3, 6, 6], d8: [5]));
var markedDodgeMatch = markedDodgeService.MovePlayer(twoTackleZoneDodgeMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(1, 2));
Assert(markedDodgeMatch.PendingReroll?.Kind == PendingRerollKind.Dodge, "failed marked dodge should offer a pending reroll");
markedDodgeMatch = markedDodgeService.ResolvePendingReroll(markedDodgeMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false);

Assert(markedDodgeMatch.Phase == MatchPhase.DefensiveTurn, "dodging into two opposing tackle zones should need worse than a 3+");
Assert(markedDodgeMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State != PlayerPitchState.Standing, "failed marked dodge should knock the player down");

var failedDodgeService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 6, 6], d8: [5]));
var failedDodgeMatch = failedDodgeService.MovePlayer(
    dodgeReadyMatch with { Ball = new BallState { CarrierPlayerId = playerToPlace.Id } },
    ruleset,
    loadedLeague.Teams[0],
    playerToPlace.Id,
    new(1, 2));
Assert(failedDodgeMatch.PendingReroll?.Kind == PendingRerollKind.Dodge, "failed dodge should offer a pending reroll before resolving failure");
failedDodgeMatch = failedDodgeService.ResolvePendingReroll(failedDodgeMatch, ruleset, loadedLeague.Teams[0], useTeamReroll: false);
var failedDodgePlayer = failedDodgeMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);

Assert(failedDodgeMatch.Phase == MatchPhase.DefensiveTurn, "failed dodge should cause a turnover");
Assert(failedDodgePlayer.State == PlayerPitchState.Casualty, "failed dodge injury roll of 10+ should injure the player");
Assert(failedDodgePlayer.Square is null, "injured player from failed dodge should be removed from the pitch");
Assert(failedDodgeMatch.Ball.Square == new PitchSquare(2, 2), "failed dodge by ball carrier should scatter the ball");

var boneHeadTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "bone-head"] } : player)
        .ToArray()
};
var boneHeadService = new MatchService(new FixedDiceRoller(d6: [1]));
var boneHeadResult = boneHeadService.MovePlayer(offensiveTurnMatch, ruleset, boneHeadTeam, playerToPlace.Id, new(3, 0));
var boneHeadPlacement = boneHeadResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id);
Assert(boneHeadPlacement.Square == new PitchSquare(0, 0), "failed Bone-head should waste the action before movement");
Assert(boneHeadPlacement.TackleZonesLost, "failed Bone-head should remove the player's tackle zones");
Assert(boneHeadResult.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Move, "failed Bone-head should still spend the declared action");

var reallyStupidTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "really-stupid"] } : player)
        .ToArray()
};
var reallyStupidMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == loadedLeague.Teams[0].Players[1].Id
            ? placement with { Square = new PitchSquare(0, 1), State = PlayerPitchState.Standing }
            : placement)
        .ToArray()
};
var reallyStupidService = new MatchService(new FixedDiceRoller(d6: [2]));
var reallyStupidResult = reallyStupidService.MovePlayer(reallyStupidMatch, ruleset, reallyStupidTeam, playerToPlace.Id, new(3, 0));
Assert(reallyStupidResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(3, 0), "Really Stupid should need only 2+ with an adjacent standing teammate");

var takeRootTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "take-root"] } : player)
        .ToArray()
};
var takeRootService = new MatchService(new FixedDiceRoller(d6: [1]));
var takeRootResult = takeRootService.MovePlayer(offensiveTurnMatch, ruleset, takeRootTeam, playerToPlace.Id, new(3, 0));
Assert(takeRootResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Rooted, "failed Take Root should mark the player rooted");
Assert(takeRootResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(0, 0), "failed Take Root should prevent movement");

var animalSavageryTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "animal-savagery"] } : player)
        .ToArray()
};
var adjacentTeammate = loadedLeague.Teams[0].Players[1];
var animalSavageryMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(0, 0), State = PlayerPitchState.Standing }
            : placement.PlayerId == adjacentTeammate.Id
                ? placement with { Square = new PitchSquare(0, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var animalSavageryService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 3, 4]));
var animalSavageryResult = animalSavageryService.MovePlayer(animalSavageryMatch, ruleset, animalSavageryTeam, playerToPlace.Id, new(1, 0));
Assert(animalSavageryResult.Placements.Single(placement => placement.PlayerId == adjacentTeammate.Id).State == PlayerPitchState.Stunned, "failed Animal Savagery should bite a nearby teammate when one is available");
Assert(animalSavageryResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 0), "Animal Savagery should continue the declared action after the bite if the turn does not end");

var furyTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "unchannelled-fury"] } : player)
        .ToArray()
};
var furyService = new MatchService(new FixedDiceRoller(d6: [1]));
var furyResult = furyService.MovePlayer(offensiveTurnMatch, ruleset, furyTeam, playerToPlace.Id, new(3, 0));
Assert(furyResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(0, 0), "failed Unchannelled Fury should waste the action");

var bloodlustTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "bloodlust"] } : player)
        .ToArray()
};
var bloodlustMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(0, 0), State = PlayerPitchState.Standing }
            : placement.PlayerId == adjacentTeammate.Id
                ? placement with { Square = new PitchSquare(0, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var bloodlustService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 3, 4]));
var bloodlustResult = bloodlustService.MovePlayer(bloodlustMatch, ruleset, bloodlustTeam, playerToPlace.Id, new(1, 0));
Assert(bloodlustResult.Placements.Single(placement => placement.PlayerId == adjacentTeammate.Id).State == PlayerPitchState.Stunned, "failed Bloodlust should bite a nearby teammate when one is available");
Assert(bloodlustResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 0), "Bloodlust should continue the declared action after the bite if the turn does not end");

var noHandsTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "no-hands"] } : player)
        .ToArray()
};
var noHandsService = new MatchService(new FixedDiceRoller(d8: [5]));
var noHandsResult = noHandsService.MovePlayer(
    offensiveTurnMatch with { Ball = new BallState { Square = new PitchSquare(2, 0) } },
    ruleset,
    noHandsTeam,
    playerToPlace.Id,
    new(2, 0));
Assert(noHandsResult.Ball.CarrierPlayerId is null, "No Hands should prevent picking up the ball");
Assert(noHandsResult.Ball.Square == new PitchSquare(3, 0), "No Hands pickup failure should bounce the ball");

var lonerTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == passReceiver.Id ? player with { Skills = [.. player.Skills, "loner", "dodge"] } : player)
        .ToArray()
};
var lonerService = new MatchService(new FixedDiceRoller(d6: [1, 1, 6, 6, 6, 6], d8: [5]));
var lonerPending = lonerService.MovePlayer(dodgeSkillMatch with { HomeRerollsRemaining = 1, TeamRerollUses = [] }, ruleset, lonerTeam, passReceiver.Id, new(1, 2), awayLeague.Teams[0]);
var lonerFailedReroll = lonerService.ResolvePendingReroll(lonerPending, ruleset, lonerTeam, useTeamReroll: true);
Assert(lonerFailedReroll.HomeRerollsRemaining == 1, "failed Loner should not spend the team reroll");
Assert(lonerFailedReroll.Phase == MatchPhase.DefensiveTurn, "failed Loner should resolve the original failed roll");

var stuntyTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "stunty"] } : player)
        .ToArray()
};
var stuntyService = new MatchService(new FixedDiceRoller(d6: [3]));
var stuntyResult = stuntyService.MovePlayer(twoTackleZoneDodgeMatch, ruleset, stuntyTeam, playerToPlace.Id, new(1, 2));
Assert(stuntyResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 2), "Stunty should ignore opposing tackle-zone modifiers when dodging");

var titchyTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "titchy"] } : player)
        .ToArray()
};
var titchyService = new MatchService(new FixedDiceRoller(d6: [2]));
var titchyResult = titchyService.MovePlayer(dodgeReadyMatch, ruleset, titchyTeam, playerToPlace.Id, new(1, 2));
Assert(titchyResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(1, 2), "Titchy should improve dodge tests by one");

smoke.StartSection("Special actions and weapons");
var specialActionMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement with { Square = null, State = PlayerPitchState.Reserve })
        .ToArray()
};

var stabTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "stab"] } : player)
        .ToArray()
};
var stabService = new MatchService(new FixedDiceRoller(d6: [6, 6, 3, 4]));
var stabResult = stabService.StabPlayer(specialActionMatch, ruleset, stabTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
Assert(stabResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Stunned, "Stab should resolve armor and injury without block dice");
Assert(stabResult.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Special, "Stab should spend a special activation");
AssertThrows(
    () => stabService.MovePlayer(stabResult, ruleset, stabTeam, playerToPlace.Id, new PitchSquare(1, 2)),
    "special actions should consume the player's activation");

var carrierStabService = new MatchService(new FixedDiceRoller(d6: [6, 6, 3, 4], d8: [5]));
var carrierStabResult = carrierStabService.StabPlayer(specialActionMatch with { Ball = new BallState { CarrierPlayerId = awayPlayerToPlace.Id } }, ruleset, stabTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
Assert(carrierStabResult.Ball.CarrierPlayerId is null && carrierStabResult.Ball.Square is not null, "special action knockdowns should drop the ball from a carrier");
AssertThrows(
    () => matchService.StabPlayer(specialActionMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id),
    "special actions should require the matching trait or weapon skill");

var chainsawTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "chainsaw"] } : player)
        .ToArray()
};
var chainsawService = new MatchService(new FixedDiceRoller(d6: [2, 3, 6, 3, 4]));
var chainsawResult = chainsawService.ChainsawPlayer(specialActionMatch, ruleset, chainsawTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
Assert(chainsawResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Stunned, "Chainsaw should add +3 to the armor roll on a successful start roll");

var projectileVomitTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "projectile-vomit"] } : player)
        .ToArray()
};
var projectileVomitService = new MatchService(new FixedDiceRoller(d6: [1, 6, 6, 3, 4]));
var projectileVomitResult = projectileVomitService.ProjectileVomitPlayer(specialActionMatch, ruleset, projectileVomitTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
Assert(projectileVomitResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Stunned, "Projectile Vomit should hit the user on a roll of 1");

var hypnoticGazeTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "hypnotic-gaze"] } : player)
        .ToArray()
};
var hypnoticGazeService = new MatchService(new FixedDiceRoller(d6: [3]));
var hypnoticGazeResult = hypnoticGazeService.HypnoticGazePlayer(specialActionMatch, ruleset, hypnoticGazeTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
Assert(hypnoticGazeResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).TackleZonesLost, "Hypnotic Gaze should remove the target's tackle zone on a successful agility test");

// A failed Hypnotic Gaze may be rerolled.
var gazeRerollService = new MatchService(new FixedDiceRoller(d6: [1, 5]));
var gazeRerollPending = gazeRerollService.HypnoticGazePlayer(specialActionMatch, ruleset, hypnoticGazeTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
Assert(gazeRerollPending.PendingReroll?.Kind == PendingRerollKind.HypnoticGaze, "a failed Hypnotic Gaze should offer a reroll");
var gazeRerolled = gazeRerollService.ResolvePendingReroll(gazeRerollPending, ruleset, hypnoticGazeTeam, useTeamReroll: true, opposingTeam: awayLeague.Teams[0]);
Assert(gazeRerolled.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).TackleZonesLost, "a successful Hypnotic Gaze reroll should remove the target's tackle zone");

var breatheFireTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "breathe-fire"] } : player)
        .ToArray()
};
var breatheFireService = new MatchService(new FixedDiceRoller(d6: [2, 6, 6, 3, 4]));
var breatheFireResult = breatheFireService.BreatheFirePlayer(specialActionMatch, ruleset, breatheFireTeam, playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
Assert(breatheFireResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Stunned, "Breathe Fire should hit on 2+ and resolve armor and injury");

var ballAndChainTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "ball-and-chain"] } : player)
        .ToArray()
};
var ballAndChainMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement with { Square = null, State = PlayerPitchState.Reserve })
        .ToArray()
};
var ballAndChainService = new MatchService(new FixedDiceRoller(d8: [5]));
var ballAndChainResult = ballAndChainService.BallAndChainMove(ballAndChainMatch, ruleset, ballAndChainTeam, playerToPlace.Id, awayLeague.Teams[0]);
Assert(ballAndChainResult.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).Square == new PitchSquare(2, 1), "Ball and Chain should move one random square when the destination is empty");

var bombTarget = awayLeague.Teams[0].Players[1];
var bombardierTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "bombardier"] } : player)
        .ToArray()
};
var bombMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == bombTarget.Id
                ? placement with { Square = new PitchSquare(3, 1), State = PlayerPitchState.Standing }
                : placement with { Square = null, State = PlayerPitchState.Reserve })
        .ToArray()
};
var bombService = new MatchService(new FixedDiceRoller(d6: [6, 1, 6, 6, 3, 4]));
var bombResult = bombService.ThrowBomb(bombMatch, ruleset, bombardierTeam, playerToPlace.Id, new PitchSquare(3, 1), awayLeague.Teams[0]);
Assert(bombResult.Placements.Single(placement => placement.PlayerId == bombTarget.Id).State == PlayerPitchState.Stunned, "Bombardier should throw, allow a catch attempt on the landing square, and explode against affected players");

var caughtBombService = new MatchService(new FixedDiceRoller(d6: [6, 6, 6, 6, 6]));
var caughtBombPending = caughtBombService.ThrowBomb(bombMatch, ruleset, bombardierTeam, playerToPlace.Id, new PitchSquare(3, 1), awayLeague.Teams[0]);
Assert(caughtBombPending.PendingBombThrow?.ThrowerPlayerId == bombTarget.Id, "a caught bomb should create a pending throw-back instead of exploding immediately");
AssertThrows(
    () => caughtBombService.AdvanceTurn(caughtBombPending, ruleset),
    "pending bomb throw-back should block turn advancement");

var thrownBackBomb = caughtBombService.ThrowPendingBomb(caughtBombPending, ruleset, awayLeague.Teams[0], bombardierTeam, new PitchSquare(1, 1));
Assert(thrownBackBomb.PendingBombThrow?.ThrowerPlayerId == playerToPlace.Id, "a thrown-back bomb caught by another player should continue the throw-back loop");

var explodedThrownBackBomb = caughtBombService.ThrowPendingBomb(thrownBackBomb, ruleset, bombardierTeam, awayLeague.Teams[0], new PitchSquare(5, 1));
Assert(explodedThrownBackBomb.PendingBombThrow is null, "a thrown-back bomb that lands without a catch should explode and clear the pending throw");
Assert(explodedThrownBackBomb.Log.Any(entry => entry.Message.Contains("Bomb explodes at 5,1", StringComparison.Ordinal)), "throw-back explosion should be logged at the final landing square");

// The active thrower may reroll a missed bomb throw.
var bombThrowRerollService = new MatchService(new FixedDiceRoller(d6: [1], d8: [5, 5, 5]));
var bombThrowPending = bombThrowRerollService.ThrowBomb(bombMatch, ruleset, bombardierTeam, playerToPlace.Id, new PitchSquare(3, 1), awayLeague.Teams[0]);
Assert(bombThrowPending.PendingReroll?.Kind == PendingRerollKind.BombThrow, "a missed bomb throw should offer the active thrower a reroll");

// A friendly (active-team) player who drops a bomb catch may reroll it.
var friendlyBombCatcher = loadedLeague.Teams[0].Players[2];
var bombCatchMatch = bombMatch with
{
    Placements = bombMatch.Placements
        .Select(placement => placement.PlayerId == friendlyBombCatcher.Id
            ? placement with { Square = new PitchSquare(3, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == bombTarget.Id
                ? placement with { Square = null, State = PlayerPitchState.Reserve }
                : placement)
        .ToArray()
};
var bombCatchRerollService = new MatchService(new FixedDiceRoller(d6: [6, 1, 6]));
var bombCatchPending = bombCatchRerollService.ThrowBomb(bombCatchMatch, ruleset, bombardierTeam, playerToPlace.Id, new PitchSquare(3, 1), awayLeague.Teams[0]);
Assert(bombCatchPending.PendingReroll?.Kind == PendingRerollKind.BombCatch, "a friendly bomb-catch miss should offer a reroll");
var bombCatchRerolled = bombCatchRerollService.ResolvePendingReroll(bombCatchPending, ruleset, bombardierTeam, useTeamReroll: true, opposingTeam: awayLeague.Teams[0]);
Assert(bombCatchRerolled.PendingBombThrow?.ThrowerPlayerId == friendlyBombCatcher.Id, "a successful bomb-catch reroll should let the friendly player catch and throw it back");

smoke.StartSection("Throw and kick team-mate");
var launchedPlayer = loadedLeague.Teams[0].Players[1];
var throwTeamMateTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id
            ? player with { Skills = [.. player.Skills, "throw-team-mate", "strong-arm"] }
            : player.Id == launchedPlayer.Id
                ? player with { Skills = [.. player.Skills, "right-stuff"] }
                : player)
        .ToArray()
};
// These launch tests exercise raw scatter/landing/crash mechanics; zero the team rerolls so a failed
// throw or landing resolves immediately instead of pausing for the (separately tested) reroll prompt.
var throwTeamMateMatch = offensiveTurnMatch with
{
    HomeRerollsRemaining = 0,
    AwayRerollsRemaining = 0,
    HomeLeaderRerollAvailable = false,
    AwayLeaderRerollAvailable = false,
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == launchedPlayer.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement with { Square = null, State = PlayerPitchState.Reserve })
        .ToArray()
};
var throwTeamMateService = new MatchService(new FixedDiceRoller(d6: [4, 3]));
var throwTeamMateResult = throwTeamMateService.ThrowTeamMate(throwTeamMateMatch, ruleset, throwTeamMateTeam, playerToPlace.Id, launchedPlayer.Id, new PitchSquare(3, 1));
Assert(throwTeamMateResult.Placements.Single(placement => placement.PlayerId == launchedPlayer.Id).Square == new PitchSquare(3, 1), "Throw Team-Mate should place a Right Stuff player on the target square after a successful throw and landing");
Assert(throwTeamMateResult.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Pass, "Throw Team-Mate should spend the team's pass action");

var firstLaunchCollisionPlayer = loadedLeague.Teams[0].Players[2];
var secondLaunchCollisionPlayer = loadedLeague.Teams[0].Players[3];
var launchCollisionMatch = throwTeamMateMatch with
{
    Placements = throwTeamMateMatch.Placements
        .Select(placement => placement.PlayerId == firstLaunchCollisionPlayer.Id
            ? placement with { Square = new PitchSquare(3, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == secondLaunchCollisionPlayer.Id
                ? placement with { Square = new PitchSquare(4, 1), State = PlayerPitchState.Standing }
                : placement)
        .ToArray()
};
var launchCollisionService = new MatchService(new FixedDiceRoller(d6: [4, 1, 1, 1, 1, 3], d8: [5, 5]));
var launchCollisionResult = launchCollisionService.ThrowTeamMate(launchCollisionMatch, ruleset, throwTeamMateTeam, playerToPlace.Id, launchedPlayer.Id, new PitchSquare(3, 1));
Assert(launchCollisionResult.Placements.Single(placement => placement.PlayerId == firstLaunchCollisionPlayer.Id).State == PlayerPitchState.Prone, "landing on an occupied square should knock down the first collision occupant");
Assert(launchCollisionResult.Placements.Single(placement => placement.PlayerId == secondLaunchCollisionPlayer.Id).State == PlayerPitchState.Prone, "launch collision scatter should continue into and knock down a second occupied square");
Assert(launchCollisionResult.Placements.Single(placement => placement.PlayerId == launchedPlayer.Id).Square == new PitchSquare(5, 1), "launch collision chains should scatter the launched player onward until a landing can be resolved");

var ineligibleLaunchTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id ? player with { Skills = [.. player.Skills, "throw-team-mate"] } : player)
        .ToArray()
};
AssertThrows(
    () => matchService.ThrowTeamMate(throwTeamMateMatch, ruleset, ineligibleLaunchTeam, playerToPlace.Id, launchedPlayer.Id, new PitchSquare(3, 1)),
    "Throw Team-Mate should require Right Stuff on the launched player");

var hungryTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id
            ? player with { Skills = [.. player.Skills, "throw-team-mate", "always-hungry"] }
            : player.Id == launchedPlayer.Id
                ? player with { Skills = [.. player.Skills, "right-stuff"] }
                : player)
        .ToArray()
};
var hungryService = new MatchService(new FixedDiceRoller(d6: [1, 1]));
var hungryResult = hungryService.ThrowTeamMate(throwTeamMateMatch with { Ball = new BallState { CarrierPlayerId = launchedPlayer.Id } }, ruleset, hungryTeam, playerToPlace.Id, launchedPlayer.Id, new PitchSquare(3, 1));
Assert(hungryResult.Placements.Single(placement => placement.PlayerId == launchedPlayer.Id).State == PlayerPitchState.Casualty, "Always Hungry double-one should remove the Right Stuff player as a casualty");
Assert(hungryResult.Phase == MatchPhase.DefensiveTurn, "Always Hungry eating a ball carrier should cause a turnover");

var swoopTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id
            ? player with { Skills = [.. player.Skills, "throw-team-mate"] }
            : player.Id == launchedPlayer.Id
                ? player with { Skills = [.. player.Skills, "right-stuff", "swoop"] }
                : player)
        .ToArray()
};
var swoopService = new MatchService(new FixedDiceRoller(d6: [1, 4, 3], d8: [5]));
var swoopResult = swoopService.ThrowTeamMate(throwTeamMateMatch, ruleset, swoopTeam, playerToPlace.Id, launchedPlayer.Id, new PitchSquare(3, 1));
Assert(swoopResult.Placements.Single(placement => placement.PlayerId == launchedPlayer.Id).Square == new PitchSquare(5, 1), "Swoop should alter inaccurate Throw Team-Mate scatter before landing");

var kickTeamMateTeam = loadedLeague.Teams[0] with
{
    Players = loadedLeague.Teams[0].Players
        .Select(player => player.Id == playerToPlace.Id
            ? player with { Skills = [.. player.Skills, "kick-team-mate"] }
            : player.Id == launchedPlayer.Id
                ? player with { Skills = [.. player.Skills, "right-stuff"] }
                : player)
        .ToArray()
};
var kickTeamMateService = new MatchService(new FixedDiceRoller(d6: [2, 3]));
var kickTeamMateResult = kickTeamMateService.KickTeamMate(throwTeamMateMatch, ruleset, kickTeamMateTeam, playerToPlace.Id, launchedPlayer.Id, new PitchSquare(3, 1));
Assert(kickTeamMateResult.Placements.Single(placement => placement.PlayerId == launchedPlayer.Id).Square == new PitchSquare(3, 1), "Kick Team-Mate should launch and land a Right Stuff team-mate");
Assert(kickTeamMateResult.Activations.Single(activation => activation.PlayerId == playerToPlace.Id).Action == PlayerTurnAction.Special, "Kick Team-Mate should spend a special activation");

var crashService = new MatchService(new FixedDiceRoller(d6: [4, 1, 6, 6, 3, 4], d8: [5]));
var crashResult = crashService.ThrowTeamMate(throwTeamMateMatch with { Ball = new BallState { CarrierPlayerId = launchedPlayer.Id } }, ruleset, throwTeamMateTeam, playerToPlace.Id, launchedPlayer.Id, new PitchSquare(3, 1));
Assert(crashResult.Placements.Single(placement => placement.PlayerId == launchedPlayer.Id).State == PlayerPitchState.Stunned, "failed landing should injure the thrown team-mate");
Assert(crashResult.Ball.CarrierPlayerId is null && crashResult.Ball.Square is not null, "failed landing by a ball carrier should scatter the ball");
Assert(crashResult.Phase == MatchPhase.DefensiveTurn, "failed landing should cause a turnover");

var touchdownThrowMatch = offensiveTurnMatch with
{
    Ball = new BallState { CarrierPlayerId = launchedPlayer.Id },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = new PitchSquare(ruleset.PitchWidth - 3, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == launchedPlayer.Id
                ? placement with { Square = new PitchSquare(ruleset.PitchWidth - 2, 1), State = PlayerPitchState.Standing }
                : placement with { Square = null, State = PlayerPitchState.Reserve })
        .ToArray()
};
var touchdownThrowService = new MatchService(new FixedDiceRoller(d6: [4, 3]));
var touchdownThrowResult = touchdownThrowService.ThrowTeamMate(touchdownThrowMatch, ruleset, throwTeamMateTeam, playerToPlace.Id, launchedPlayer.Id, new PitchSquare(ruleset.PitchWidth - 1, 1));
Assert(touchdownThrowResult.HomeScore == offensiveTurnMatch.HomeScore + 1, "a successfully landed thrown ball carrier in the end zone should score");
Assert(touchdownThrowResult.Phase == MatchPhase.DefenseSetup, "throw-team-mate touchdown should begin the next drive setup");

// The throw and the landing are separate rolls and each may be rerolled. Restore rerolls on the launch
// match (the mechanic tests above zero them) so the prompts appear.
var ttmRerollMatch = throwTeamMateMatch with { HomeRerollsRemaining = 2, AwayRerollsRemaining = 2 };
var ttmThrowRerollService = new MatchService(new FixedDiceRoller(d6: [1, 4, 3]));
var ttmThrowPending = ttmThrowRerollService.ThrowTeamMate(ttmRerollMatch, ruleset, throwTeamMateTeam, playerToPlace.Id, launchedPlayer.Id, new PitchSquare(3, 1));
Assert(ttmThrowPending.PendingReroll?.Kind == PendingRerollKind.ThrowTeamMate, "a missed Throw Team-Mate throw should offer the thrower a reroll");
var ttmThrowRerolled = ttmThrowRerollService.ResolvePendingReroll(ttmThrowPending, ruleset, throwTeamMateTeam, useTeamReroll: true);
Assert(ttmThrowRerolled.Placements.Single(placement => placement.PlayerId == launchedPlayer.Id).Square == new PitchSquare(3, 1), "a successful Throw Team-Mate reroll should land the player on target");

var ttmLandingRerollService = new MatchService(new FixedDiceRoller(d6: [4, 1, 5]));
var ttmLandingPending = ttmLandingRerollService.ThrowTeamMate(ttmRerollMatch, ruleset, throwTeamMateTeam, playerToPlace.Id, launchedPlayer.Id, new PitchSquare(3, 1));
Assert(ttmLandingPending.PendingReroll?.Kind == PendingRerollKind.Landing, "a failed landing after a Throw Team-Mate should offer a reroll");
var ttmLandingRerolled = ttmLandingRerollService.ResolvePendingReroll(ttmLandingPending, ruleset, throwTeamMateTeam, useTeamReroll: true);
var ttmLandingPlacement = ttmLandingRerolled.Placements.Single(placement => placement.PlayerId == launchedPlayer.Id);
Assert(ttmLandingPlacement.Square == new PitchSquare(3, 1) && ttmLandingPlacement.State == PlayerPitchState.Standing, "a successful landing reroll should stand the thrown player on the target");

smoke.StartSection("Turn advancement, halftime, and full time");
var defensiveTurnMatch = matchService.AdvancePhase(movedMatch);
Assert(defensiveTurnMatch.Phase == MatchPhase.DefensiveTurn, "offensive player turn should advance to defensive turn");
Assert(defensiveTurnMatch.ActiveTeamId == awayLeague.Teams[0].Id, "away team should have the defensive turn");
Assert(defensiveTurnMatch.HomeTurn == 2 && defensiveTurnMatch.AwayTurn == 1, "ending the offensive turn should consume home turn one");
Assert(defensiveTurnMatch.Turn == 1, "defensive turn should use the active team's turn counter");

var rulesetAwareDefensiveTurnMatch = matchService.AdvanceTurn(movedMatch, ruleset);
Assert(rulesetAwareDefensiveTurnMatch.Phase == MatchPhase.DefensiveTurn, "ruleset-aware turn control should end the offensive player turn");
Assert(rulesetAwareDefensiveTurnMatch.HomeTurn == 2 && rulesetAwareDefensiveTurnMatch.AwayTurn == 1, "ruleset-aware offensive turn end should consume the active team's turn");

var stunnedHomeTurnMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { State = PlayerPitchState.Stunned, StunnedRecoveryHalf = 1, StunnedRecoveryTurn = 2 }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { State = PlayerPitchState.Stunned, StunnedRecoveryHalf = 1, StunnedRecoveryTurn = 1 }
                : placement)
        .ToArray()
};
var stunnedRecoveryMatch = matchService.AdvanceTurn(stunnedHomeTurnMatch, ruleset);

Assert(stunnedRecoveryMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Stunned, "a player stunned during their own turn should not recover at the end of that same turn");
Assert(stunnedRecoveryMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Stunned, "ending a team turn should not recover the opposing team's stunned players");

var nextHomeTurnWithStunnedPlayer = stunnedRecoveryMatch with
{
    Phase = MatchPhase.OffensivePlayerTurn,
    ActiveTeamId = loadedLeague.Teams[0].Id,
    Turn = 2
};
var stunnedRecoveredAfterFullTurn = matchService.AdvanceTurn(nextHomeTurnWithStunnedPlayer, ruleset);
Assert(stunnedRecoveredAfterFullTurn.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Prone, "a stunned player should recover to prone after spending their next own team turn stunned");

var awayStunnedRecoveryMatch = matchService.AdvanceTurn(stunnedRecoveryMatch, ruleset);
Assert(awayStunnedRecoveryMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "a player stunned during the opponent turn should recover after their upcoming own turn");

var defensiveMoveMatch = matchService.MovePlayer(defensiveTurnMatch, ruleset, awayLeague.Teams[0], awayPlayerToPlace.Id, new(19, 5));
var defensiveMovedPlayer = defensiveMoveMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id);
Assert(defensiveMovedPlayer.Square == new PitchSquare(19, 5), "defensive player should move during defensive turn");

var nextOffensiveTurnMatch = matchService.AdvanceTurn(defensiveMoveMatch, ruleset);
Assert(nextOffensiveTurnMatch.Phase == MatchPhase.OffensivePlayerTurn, "defensive turn should advance to next offensive player turn");
Assert(nextOffensiveTurnMatch.Turn == 2, "turn should increment after defensive turn");
Assert(nextOffensiveTurnMatch.HomeTurn == 2 && nextOffensiveTurnMatch.AwayTurn == 2, "both teams should be on turn two after each has acted once");
Assert(nextOffensiveTurnMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "offense should regain the next player turn");

var lastFirstHalfDefensiveTurn = offensiveTurnMatch with
{
    Half = 1,
    HomeTurn = ruleset.TurnsPerHalf + 1,
    AwayTurn = ruleset.TurnsPerHalf,
    Phase = MatchPhase.DefensiveTurn,
    ActiveTeamId = awayLeague.Teams[0].Id,
    FirstHalfReceivingTeamId = loadedLeague.Teams[0].Id
};
var knockoutHalftimeMatch = lastFirstHalfDefensiveTurn with
{
    HomeRerollsRemaining = 0,
    AwayRerollsRemaining = 0,
    Placements = lastFirstHalfDefensiveTurn.Placements
        .Select(placement => placement.PlayerId == playerToPlace.Id
            ? placement with { Square = null, State = PlayerPitchState.KnockedOut }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = null, State = PlayerPitchState.KnockedOut }
                : placement)
        .ToArray()
};
var halftimeService = new MatchService(new FixedDiceRoller(d6: [4, 2]));
var secondHalfSetupMatch = halftimeService.AdvanceTurn(knockoutHalftimeMatch, ruleset);

Assert(secondHalfSetupMatch.Half == 2, "both teams finishing eight turns should advance to the second half");
Assert(secondHalfSetupMatch.HomeTurn == 1 && secondHalfSetupMatch.AwayTurn == 1, "second half should reset both team turn counters");
Assert(secondHalfSetupMatch.Phase == MatchPhase.DefenseSetup, "second half should begin with defense placement");
Assert(secondHalfSetupMatch.ActiveTeamId == loadedLeague.Teams[0].Id, "first-half receiving team should kick off to start the second half");
Assert(secondHalfSetupMatch.Ball.CarrierPlayerId is null && secondHalfSetupMatch.Ball.Square is null, "halftime should clear the ball");
Assert(secondHalfSetupMatch.HomeRerollsRemaining == loadedLeague.Teams[0].Rerolls && secondHalfSetupMatch.AwayRerollsRemaining == awayLeague.Teams[0].Rerolls, "halftime should refresh both teams' rerolls");
Assert(secondHalfSetupMatch.Placements.Single(placement => placement.PlayerId == playerToPlace.Id).State == PlayerPitchState.Reserve, "halftime should recover knocked out players on 4+");
Assert(secondHalfSetupMatch.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.KnockedOut, "halftime should leave failed knockout recoveries knocked out");

var lastFirstHalfOffensiveTurn = offensiveTurnMatch with
{
    Half = 1,
    HomeTurn = ruleset.TurnsPerHalf,
    AwayTurn = ruleset.TurnsPerHalf + 1,
    Phase = MatchPhase.OffensivePlayerTurn,
    ActiveTeamId = loadedLeague.Teams[0].Id,
    FirstHalfReceivingTeamId = loadedLeague.Teams[0].Id
};
var secondHalfFromOffensiveEnd = matchService.AdvanceTurn(lastFirstHalfOffensiveTurn, ruleset);

Assert(secondHalfFromOffensiveEnd.Half == 2, "ruleset-aware offensive turn end should advance the half when both teams are done");
Assert(secondHalfFromOffensiveEnd.Phase == MatchPhase.DefenseSetup, "ruleset-aware offensive turn end should begin second-half setup when the half ends");

var lastSecondHalfDefensiveTurn = offensiveTurnMatch with
{
    Half = 2,
    HomeTurn = ruleset.TurnsPerHalf + 1,
    AwayTurn = ruleset.TurnsPerHalf,
    Phase = MatchPhase.DefensiveTurn,
    ActiveTeamId = awayLeague.Teams[0].Id
};
var fullTimeMatch = matchService.AdvanceTurn(lastSecondHalfDefensiveTurn, ruleset);

Assert(fullTimeMatch.Phase == MatchPhase.Complete, "both teams finishing eight second-half turns should complete the match");

smoke.StartSection("Validation and illegal-action guards");
AssertThrows(
    () => leagueService.AddTeam(
        league,
        ruleset,
        "Too Few",
        "Tester",
        humanRoster,
        Enumerable.Range(1, 10).Select(index => new PlayerDraftPick($"Lineman {index}", "lineman")),
        rerolls: 2),
    "drafts below players-per-side should fail");

AssertThrows(
    () => leagueService.AddTeam(
        league,
        ruleset,
        "Too Many Players",
        "Tester",
        humanRoster,
        Enumerable.Range(1, 17).Select(index => new PlayerDraftPick($"Lineman {index}", "lineman")),
        rerolls: 0),
    "drafts above sixteen players should fail");

AssertThrows(
    () => leagueService.AddTeam(
        league,
        ruleset,
        "Too Many Rerolls",
        "Tester",
        humanRoster,
        Enumerable.Range(1, 11).Select(index => new PlayerDraftPick($"Lineman {index}", "lineman")),
        rerolls: ruleset.RerollCap + 1),
    "drafts above reroll cap should fail");

AssertThrows(
    () => leagueService.CreateLeague("Too Small", ruleset, [rosterSet], targetTeamCount: 1),
    "leagues should require at least two teams");

AssertThrows(
    () => leagueService.CreateLeague("Odd League", ruleset, [rosterSet], targetTeamCount: 3),
    "league scheduling should require an even number of teams");

AssertThrows(
    () => matchService.CreateHotseatMatch(ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0]),
    "matches should require two different teams");

AssertThrows(
    () => matchService.CreateHotseatMatch(ruleset, depletedTeam with { Players = depletedTeam.Players.Take(2).ToArray() }, awayLeague.Teams[0]),
    "matches should require at least three players");

AssertThrows(
    () => matchService.PlacePlayer(defenseSetupMatch, ruleset, awayLeague.Teams[0].Players[1].Id, new(20, 5)),
    "placement should reject occupied squares");

AssertThrows(
    () => matchService.PlacePlayer(loadedMatch, ruleset, awayPlayerToPlace.Id, new(5, 5)),
    "defense placement should reject the wrong side of the pitch");

var wideZoneLimitMatch = matchService.PlacePlayer(
    matchService.PlacePlayer(loadedMatch, ruleset, awayLeague.Teams[0].Players[0].Id, new(13, 0)),
    ruleset,
    awayLeague.Teams[0].Players[1].Id,
    new(14, 0));

AssertThrows(
    () => matchService.PlacePlayer(wideZoneLimitMatch, ruleset, awayLeague.Teams[0].Players[2].Id, new(15, 1)),
    "setup should reject more than two players in the same wide zone");

var benchDefenseSetup = SetupTeam(matchService, benchMatch, ruleset, awayLeague.Teams[0], [
    new(20, 5),
    new(13, 4),
    new(13, 5),
    new(13, 6),
    new(20, 4),
    new(20, 6),
    new(20, 7),
    new(20, 8),
    new(20, 9),
    new(20, 10),
    new(20, 11)
]);
var benchOffenseSetup = matchService.AdvancePhase(benchDefenseSetup, ruleset);
var elevenBenchPlayersSetup = SetupTeam(matchService, benchOffenseSetup, ruleset, benchLeague.Teams[0], [
    new(0, 0),
    new(12, 4),
    new(12, 5),
    new(12, 6),
    new(1, 4),
    new(1, 5),
    new(1, 6),
    new(1, 7),
    new(1, 8),
    new(1, 9),
    new(1, 10)
]);
AssertThrows(
    () => matchService.PlacePlayer(elevenBenchPlayersSetup, ruleset, benchLeague.Teams[0].Players[11].Id, new(2, 11)),
    "setup should reject placing a twelfth player");

var setupWithReturnedReserve = matchService.ReturnSetupPlayerToReserve(elevenBenchPlayersSetup, benchLeague.Teams[0].Players[0].Id);
Assert(setupWithReturnedReserve.Placements.Single(placement => placement.PlayerId == benchLeague.Teams[0].Players[0].Id).State == PlayerPitchState.Reserve, "setup should allow returning a placed player to reserve");
var swappedSetupPlayer = matchService.PlacePlayer(setupWithReturnedReserve, ruleset, benchLeague.Teams[0].Players[11].Id, new(2, 11));
Assert(swappedSetupPlayer.Placements.Single(placement => placement.PlayerId == benchLeague.Teams[0].Players[11].Id).Square == new PitchSquare(2, 11), "setup should allow placing a reserve player after returning another player to reserve");

AssertThrows(
    () => matchService.PlacePlayer(loadedMatch, ruleset, awayPlayerToPlace.Id, new(-1, 0)),
    "placement should reject squares outside the pitch");

AssertThrows(
    () => matchService.PlacePlayer(loadedMatch, ruleset, playerToPlace.Id, new(0, 0)),
    "placement should reject inactive setup teams");

AssertThrows(
    () => matchService.MovePlayer(placedMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(3, 0)),
    "movement should reject setup phases");

AssertThrows(
    () => matchService.BlockPlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id),
    "blocking should reject non-adjacent players");

AssertThrows(
    () => matchService.HandOffBall(
        handOffReadyMatch with
        {
            Placements = handOffReadyMatch.Placements
                .Select(placement => placement.PlayerId == handOffReceiver.Id
                    ? placement with { Square = new PitchSquare(5, 1), State = PlayerPitchState.Standing }
                    : placement)
                .ToArray()
        },
        ruleset,
        loadedLeague.Teams[0],
        playerToPlace.Id,
        handOffReceiver.Id),
    "handoff should require adjacent players");

AssertThrows(
    () => matchService.HandOffBall(handOffMatch, ruleset, loadedLeague.Teams[0], handOffReceiver.Id, loadedLeague.Teams[0].Players[2].Id),
    "handoff should be limited to once per turn");

AssertThrows(
    () => passService.PassBall(completedPassMatch, ruleset, loadedLeague.Teams[0], passReceiver.Id, loadedLeague.Teams[0].Players[1].Id),
    "pass should be limited to once per turn");

AssertThrows(
    () => matchService.PassBall(
        passReadyMatch with
        {
            Placements = passReadyMatch.Placements
                .Select(placement => placement.PlayerId == passReceiver.Id
                    ? placement with { Square = new PitchSquare(20, 1), State = PlayerPitchState.Standing }
                    : placement)
                .ToArray()
        },
        ruleset,
        loadedLeague.Teams[0],
        passerPlayer.Id,
        passReceiver.Id),
    "pass should reject receivers beyond long bomb range");

AssertThrows(
    () => pendingInterceptionService.ChooseInterceptor(pendingInterceptionMatch, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], awayLeague.Teams[0].Players[2].Id),
    "interception choice should reject ineligible defenders");

AssertThrows(
    () => matchService.BlockPlayer(movedMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, awayLeague.Teams[0], awayPlayerToPlace.Id),
    "blocking should reject a second activation in the same turn");

AssertThrows(
    () => matchService.BlitzPlayer(blitzMatch, ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0].Players[1].Id, new(2, 2), awayLeague.Teams[0], awayPlayerToPlace.Id),
    "blitz should be limited to once per turn");

AssertThrows(
    () => matchService.MovePlayer(movedMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(10, 0)),
    "continued movement should reject destinations beyond remaining movement plus go-for-it allowance");

AssertThrows(
    () => matchService.MovePlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(10, 0)),
    "movement should reject destinations beyond movement plus go-for-it allowance");

AssertThrows(
    () => matchService.MovePlayer(offensiveTurnMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(20, 5)),
    "movement should reject occupied destinations");

AssertThrows(
    () => matchService.MovePlayer(loadedMatch, ruleset, loadedLeague.Teams[0], loadedLeague.Teams[0].Players[1].Id, new(1, 1)),
    "movement should reject reserve players");

AssertThrows(
    () => matchService.MovePlayer(
        offensiveTurnMatch,
        ruleset,
        awayLeague.Teams[0],
        awayPlayerToPlace.Id,
        new(19, 5)),
    "movement should reject inactive teams during a turn");

AssertThrows(
    () => matchService.MovePlayer(
        defensiveTurnMatch,
        ruleset,
        loadedLeague.Teams[0],
        playerToPlace.Id,
        new(4, 0)),
    "movement should reject offensive team during defensive turn");

smoke.StartSection("Scenario regression and seeded playtests");
var scenarioDriveService = new MatchService(new FixedDiceRoller(d6: [3, 3, 3, 3, 1, 6, 6], d8: [5]));
var scenarioDriveMatch = scenarioDriveService.CreateHotseatMatch(ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
scenarioDriveMatch = SetupTeam(scenarioDriveService, scenarioDriveMatch, ruleset, awayLeague.Teams[0], [
    new(20, 5),
    new(13, 4),
    new(13, 5),
    new(13, 6),
    new(20, 4),
    new(20, 6),
    new(20, 7),
    new(20, 8),
    new(20, 9),
    new(20, 10),
    new(20, 11)
]);
scenarioDriveMatch = scenarioDriveService.AdvancePhase(scenarioDriveMatch, ruleset);
scenarioDriveMatch = SetupTeam(scenarioDriveService, scenarioDriveMatch, ruleset, loadedLeague.Teams[0], [
    new(12, 0),
    new(12, 4),
    new(12, 5),
    new(12, 6),
    new(1, 4),
    new(1, 5),
    new(1, 6),
    new(1, 7),
    new(1, 8),
    new(1, 9),
    new(1, 10)
]);
scenarioDriveMatch = scenarioDriveService.AdvancePhase(scenarioDriveMatch, ruleset);
scenarioDriveMatch = scenarioDriveService.ResolveKickoff(scenarioDriveMatch, ruleset, loadedLeague.Teams[0], new(ruleset.PitchWidth / 2, 0), awayLeague.Teams[0]);
Assert(scenarioDriveMatch.PendingBallPlacement?.Reason == "Touchback", "scenario drive should start with a touchback choice");
scenarioDriveMatch = scenarioDriveService.ChooseBallPlacement(scenarioDriveMatch, loadedLeague.Teams[0], new(12, 0));
Assert(scenarioDriveMatch.Ball.CarrierPlayerId == playerToPlace.Id, "scenario drive should start with a touchback to the Human ball carrier");
scenarioDriveMatch = scenarioDriveService.MovePlayer(scenarioDriveMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(20, 0));
AssertMatchInvariants(scenarioDriveMatch, ruleset, "scenario drive after first Human carry");
scenarioDriveMatch = scenarioDriveService.AdvanceTurn(scenarioDriveMatch, ruleset);
scenarioDriveMatch = scenarioDriveService.AdvanceTurn(scenarioDriveMatch, ruleset);
scenarioDriveMatch = scenarioDriveService.MovePlayer(scenarioDriveMatch, ruleset, loadedLeague.Teams[0], playerToPlace.Id, new(ruleset.PitchWidth - 1, 0));
Assert(scenarioDriveMatch.HomeScore == 1 && scenarioDriveMatch.Drive == 2 && scenarioDriveMatch.Phase == MatchPhase.DefenseSetup, "scenario drive should run from kickoff through a Human touchdown and next-drive setup");
Assert(scenarioDriveMatch.PlayerAwards.Any(award => award.Kind == MatchPlayerAwardKind.Touchdown && award.PlayerId == playerToPlace.Id), "scenario drive should award touchdown SPP to the scorer");
AssertMatchInvariants(scenarioDriveMatch, ruleset, "scenario drive after touchdown reset");

var humanBlitzer = loadedLeague.Teams[0].Players[8];
var humanCatcher = loadedLeague.Teams[0].Players[7];
var humanThrower = loadedLeague.Teams[0].Players[6];
var humanOrcBlockMatch = offensiveTurnMatch with
{
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == humanBlitzer.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == awayPlayerToPlace.Id
                ? placement with { Square = new PitchSquare(2, 1), State = PlayerPitchState.Standing }
                : placement.PlayerId == playerToPlace.Id
                    ? placement with { Square = new PitchSquare(0, 0), State = PlayerPitchState.Standing }
                    : placement)
        .ToArray()
};
var humanOrcBlockService = new MatchService(new FixedDiceRoller(d6: [6, 1, 1]));
var humanOrcPendingPush = humanOrcBlockService.BlockPlayer(humanOrcBlockMatch, ruleset, loadedLeague.Teams[0], humanBlitzer.Id, awayLeague.Teams[0], awayPlayerToPlace.Id);
humanOrcPendingPush = ConfirmBlockDie(humanOrcBlockService, humanOrcPendingPush, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
var humanOrcBlockResult = humanOrcBlockService.ChoosePushSquare(humanOrcPendingPush, ruleset, loadedLeague.Teams[0], awayLeague.Teams[0], new PitchSquare(3, 1));
Assert(humanOrcBlockResult.Placements.Single(placement => placement.PlayerId == awayPlayerToPlace.Id).State == PlayerPitchState.Prone, "Human blitzer should knock down an Orc lineman in a common block pattern");
Assert(humanOrcBlockResult.Activations.Single(activation => activation.PlayerId == humanBlitzer.Id).Action == PlayerTurnAction.Block, "Human vs Orc block pattern should record the blocker action");

var humanOrcPassMatch = offensiveTurnMatch with
{
    Ball = new BallState { CarrierPlayerId = humanThrower.Id },
    Placements = offensiveTurnMatch.Placements
        .Select(placement => placement.PlayerId == humanThrower.Id
            ? placement with { Square = new PitchSquare(1, 1), State = PlayerPitchState.Standing }
            : placement.PlayerId == humanCatcher.Id
                ? placement with { Square = new PitchSquare(5, 1), State = PlayerPitchState.Standing }
                : placement.PlayerId == playerToPlace.Id
                    ? placement with { Square = new PitchSquare(0, 0), State = PlayerPitchState.Standing }
                    : placement.PlayerId == awayPlayerToPlace.Id
                        ? placement with { Square = new PitchSquare(8, 8), State = PlayerPitchState.Standing }
                        : placement)
        .ToArray()
};
var humanOrcPassService = new MatchService(new FixedDiceRoller(d6: [4, 4]));
var humanOrcPassResult = humanOrcPassService.PassBall(humanOrcPassMatch, ruleset, loadedLeague.Teams[0], humanThrower.Id, humanCatcher.Id, awayLeague.Teams[0]);
Assert(humanOrcPassResult.Ball.CarrierPlayerId == humanCatcher.Id, "Human thrower to catcher pattern should complete against Orc opposition");
Assert(humanOrcPassResult.PlayerAwards.Any(award => award.Kind == MatchPlayerAwardKind.Completion && award.PlayerId == humanThrower.Id), "Human thrower should receive completion SPP in the pass pattern");
AssertMatchInvariants(humanOrcPassResult, ruleset, "Human vs Orc pass pattern");

for (var seed = 1; seed <= 12; seed++)
{
    var seededService = new MatchService(new FixedDiceRoller(d6: [3, 3, 3, 3, 1, 6, 6], d8: [5]));
    var seededMatch = seededService.CreateHotseatMatch(ruleset, loadedLeague.Teams[0], awayLeague.Teams[0]);
    var seededDefense = SetupTeam(seededService, seededMatch, ruleset, awayLeague.Teams[0], [
        new(20, 5),
        new(13, 4),
        new(13, 5),
        new(13, 6),
        new(20, 4),
        new(20, 6),
        new(20, 7),
        new(20, 8),
        new(20, 9),
        new(20, 10),
        new(20, 11)
    ]);
    var seededOffense = seededService.AdvancePhase(seededDefense, ruleset);
    seededOffense = SetupTeam(seededService, seededOffense, ruleset, loadedLeague.Teams[0], [
        new(12, seed % 3),
        new(12, 4),
        new(12, 5),
        new(12, 6),
        new(1, 4),
        new(1, 5),
        new(1, 6),
        new(1, 7),
        new(1, 8),
        new(1, 9),
        new(1, 10)
    ]);
    var seededKickoff = seededService.AdvancePhase(seededOffense, ruleset);
    var seededTurn = seededService.ResolveKickoff(seededKickoff, ruleset, loadedLeague.Teams[0], new(ruleset.PitchWidth / 2, seed % 3), awayLeague.Teams[0]);
    if (seededTurn.PendingBallPlacement?.Reason == "Touchback")
    {
        seededTurn = seededService.ChooseBallPlacement(seededTurn, loadedLeague.Teams[0], seededTurn.PendingBallPlacement.LegalSquares[0]);
    }

    var seededCarryTarget = new PitchSquare(14 + (seed % 5), seed % 3);
    seededTurn = seededService.MovePlayer(seededTurn, ruleset, loadedLeague.Teams[0], playerToPlace.Id, seededCarryTarget);
    AssertMatchInvariants(seededTurn, ruleset, $"seeded playtest {seed} after carry");
    var seededDefenseTurn = seededService.AdvanceTurn(seededTurn, ruleset);
    AssertMatchInvariants(seededDefenseTurn, ruleset, $"seeded playtest {seed} after turn handoff");
}

smoke.PrintSummary();
Console.WriteLine("SoloBB smoke checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

// A single-die block now presents the rolled die for confirmation, exactly like a 2-die block.
// This helper confirms that single die so tests can continue asserting the resolved outcome.
// It is a no-op for multi-die blocks (the test chooses a specific die) or already-resolved states.
static MatchState ConfirmBlockDie(MatchService service, MatchState match, Ruleset ruleset, LeagueTeam attackerTeam, LeagueTeam defenderTeam)
{
    if (match.PendingBlock is PendingBlockChoice pending && pending.Rolls.Count == 1)
    {
        return service.ChooseBlockDie(match, ruleset, attackerTeam, defenderTeam, pending.Rolls[0]);
    }

    return match;
}

static void AssertThrows(Action action, string message)
{
    try
    {
        action();
    }
    catch (InvalidOperationException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void AssertThrowsInvalidData(Action action, string message)
{
    try
    {
        action();
    }
    catch (InvalidDataException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static bool HasHook(Ruleset ruleset, string skillId, GameEventKind eventKind, GameEventStage stage)
{
    return ruleset.Skills
        .Single(skill => skill.Id == skillId)
        .Hooks
        .Any(hook => hook.Event == eventKind && hook.Stage == stage);
}

static MatchState SetupTeam(MatchService matchService, MatchState match, Ruleset ruleset, LeagueTeam team, IReadOnlyList<PitchSquare> squares)
{
    var next = match;
    for (var index = 0; index < squares.Count; index++)
    {
        next = matchService.PlacePlayer(next, ruleset, team.Players[index].Id, squares[index]);
    }

    return next;
}

static void AssertMatchInvariants(MatchState match, Ruleset ruleset, string context)
{
    Assert(!(match.Ball.CarrierPlayerId is not null && match.Ball.Square is not null), $"{context}: ball cannot have both a carrier and a square");
    if (match.Ball.Square is PitchSquare ballSquare)
    {
        Assert(IsOnPitch(ruleset, ballSquare), $"{context}: loose ball must stay on the pitch");
    }

    var occupiedSquares = match.Placements
        .Where(placement => placement.Square is not null && placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned)
        .Select(placement => placement.Square!)
        .ToArray();
    Assert(occupiedSquares.All(square => IsOnPitch(ruleset, square)), $"{context}: all occupied player squares must stay on the pitch");
    Assert(occupiedSquares.Distinct().Count() == occupiedSquares.Length, $"{context}: player squares must not overlap");

    if (match.Ball.CarrierPlayerId is Guid carrierId)
    {
        Assert(match.Placements.Any(placement =>
            placement.PlayerId == carrierId &&
            placement.Square is not null &&
            placement.State == PlayerPitchState.Standing), $"{context}: ball carrier must be a standing player on the pitch");
    }

    if (match.Phase is not MatchPhase.Complete)
    {
        Assert(match.ActiveTeamId == match.HomeTeamId || match.ActiveTeamId == match.AwayTeamId, $"{context}: active team must belong to the match");
    }

    Assert(match.HomeScore >= 0 && match.AwayScore >= 0, $"{context}: scores cannot be negative");
    Assert(match.HomeTurn >= 1 && match.AwayTurn >= 1, $"{context}: turn counters cannot drop below one");
}

static bool IsOnPitch(Ruleset ruleset, PitchSquare square)
{
    return square.X >= 0 && square.X < ruleset.PitchWidth && square.Y >= 0 && square.Y < ruleset.PitchHeight;
}

public sealed record SmokeFixture(
    string Root,
    JsonGameDataStore Store,
    Ruleset Ruleset,
    RosterSet RosterSet)
{
    public TeamRoster HumanRoster => RosterSet.Rosters.Single(roster => roster.Id == "human");

    public TeamRoster OrcRoster => RosterSet.Rosters.Single(roster => roster.Id == "orc");

    public static async Task<SmokeFixture> LoadAsync()
    {
        var root = FindRoot();
        var store = new JsonGameDataStore();
        var ruleset = await store.LoadRulesetAsync(Path.Combine(root, "data", "rulesets", "bb2020-lite.json"));
        var rosterSet = await store.LoadRosterSetAsync(Path.Combine(root, "data", "rosters", "core-teams.json"), ruleset);

        return new SmokeFixture(root, store, ruleset, rosterSet);
    }

    private static string FindRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "project.godot")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

public sealed class SmokeRun
{
    private readonly List<string> _sections = [];

    public void StartSection(string name)
    {
        _sections.Add(name);
        Console.WriteLine($"[smoke] {name}");
    }

    public void PrintSummary()
    {
        Console.WriteLine($"[smoke] Completed {_sections.Count} rule-family sections:");
        foreach (var section in _sections)
        {
            Console.WriteLine($"[smoke] - {section}");
        }
    }
}

public sealed class FixedDiceRoller : IDiceRoller
{
    private readonly Queue<int> _d6;
    private readonly Queue<int> _d8;
    private readonly Queue<int> _d16;

    public FixedDiceRoller(IEnumerable<int>? d6 = null, IEnumerable<int>? d8 = null, IEnumerable<int>? d16 = null)
    {
        _d6 = new Queue<int>(d6 ?? [6]);
        _d8 = new Queue<int>(d8 ?? [1]);
        _d16 = new Queue<int>(d16 ?? [1]);
    }

    public int RollD6()
    {
        return _d6.Count > 0 ? _d6.Dequeue() : 6;
    }

    public int RollD8()
    {
        return _d8.Count > 0 ? _d8.Dequeue() : 1;
    }

    public int RollD16()
    {
        return _d16.Count > 0 ? _d16.Dequeue() : 1;
    }
}
