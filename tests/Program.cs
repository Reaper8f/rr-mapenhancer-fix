using Mono.Cecil;
using Mono.Cecil.Cil;
using MapEnhancer;
using System.Text.Json;

if (args.Length is not 2 and not 3)
    throw new ArgumentException("Usage: MapEnhancer.Checks <game Managed directory> <MapEnhancer.dll> [PrettyConvenientMenu.dll]");
var managed = Path.GetFullPath(args[0]);
var modPath = Path.GetFullPath(args[1]);
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(managed);
resolver.AddSearchDirectory(Path.GetDirectoryName(modPath)!);
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Railroader.dll"), new ReaderParameters { AssemblyResolver = resolver });
using var mod = AssemblyDefinition.ReadAssembly(modPath, new ReaderParameters { AssemblyResolver = resolver });
int checks = 0;
void Require(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> roots)
{
    foreach (var root in roots) { yield return root; foreach (var nested in Types(root.NestedTypes)) yield return nested; }
}
var allModTypes = Types(mod.MainModule.Types).ToList();
var allGameTypes = Types(game.MainModule.Types).ToDictionary(t => t.FullName);
string Element(TypeReference type) => type is ByReferenceType reference ? reference.ElementType.FullName : type.FullName;
IEnumerable<CustomAttribute> Patches(Mono.Cecil.ICustomAttributeProvider provider) =>
    provider.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");

int patches = 0;
foreach (var type in allModTypes)
foreach (var method in type.Methods.Where(m => m.Name is "Prefix" or "Postfix" or "Transpiler" ||
    m.CustomAttributes.Any(a => a.AttributeType.Name is "HarmonyPrefix" or "HarmonyPostfix" or "HarmonyTranspiler")))
{
    string? targetType = null, targetName = null;
    string[]? parameterTypes = null;
    int methodKind = 0;
    foreach (var attribute in Patches(type).Concat(Patches(method)))
    foreach (var argument in attribute.ConstructorArguments)
    {
        if (argument.Type.FullName == "System.Type") targetType = ((TypeReference)argument.Value).FullName;
        else if (argument.Type.FullName == "System.String") targetName = (string)argument.Value;
        else if (argument.Type.FullName == "HarmonyLib.MethodType") methodKind = Convert.ToInt32(argument.Value);
        else if (argument.Type.FullName == "System.Type[]")
            parameterTypes = ((CustomAttributeArgument[])argument.Value).Select(a => ((TypeReference)a.Value).FullName).ToArray();
    }
    Require(targetType != null && targetName != null, "Missing explicit patch target for " + method.FullName);
    Require(allGameTypes.ContainsKey(targetType!), "Unknown patch type " + targetType);
    if (methodKind == 1) targetName = "get_" + targetName;
    if (methodKind == 2) targetName = "set_" + targetName;
    var candidates = allGameTypes[targetType!].Methods.Where(m => m.Name == targetName &&
        (parameterTypes == null || m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameterTypes))).ToList();
    Require(candidates.Count == 1, "Ambiguous or missing patch target " + targetType + "." + targetName);
    var target = candidates[0];
    if (method.Name != "Transpiler" && !method.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyTranspiler"))
    foreach (var parameter in method.Parameters)
    {
        var name = parameter.Name;
        if (name == "__instance")
        {
            var baseType = target.DeclaringType;
            var compatible = false;
            while (baseType != null)
            {
                if (baseType.FullName == Element(parameter.ParameterType)) { compatible = true; break; }
                baseType = baseType.BaseType?.Resolve();
            }
            Require(compatible, "Incompatible __instance in " + method.FullName);
        }
        else if (name == "__result")
            Require(Element(parameter.ParameterType) == target.ReturnType.FullName, "Incorrect __result in " + method.FullName);
        else if (name.StartsWith("___"))
        {
            string fieldName = name.Substring(3);
            var field = target.DeclaringType.Fields.SingleOrDefault(f => f.Name == fieldName);
            Require(field != null && field.FieldType.FullName == Element(parameter.ParameterType),
                "Missing or incompatible injected field " + fieldName);
        }
        else if (name is not "__state" and not "__originalMethod" and not "__runOriginal")
        {
            var originalParameter = target.Parameters.SingleOrDefault(p => p.Name == name);
            Require(originalParameter != null && Element(originalParameter.ParameterType) == Element(parameter.ParameterType),
                "Missing or incompatible target parameter " + name + " in " + method.FullName);
        }
    }
    patches++;
}

// Verify that Unity callbacks actually drive the deferred lifecycle, and that
// readiness/unload callbacks never reach the unsafe map singleton getter.
var enhancer = allModTypes.Single(t => t.FullName == "MapEnhancer.MapEnhancer");
List<MethodReference> LifecycleCalls(string name) => enhancer.Methods.Single(m => m.Name == name)
    .Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToList();
var updateCalls = LifecycleCalls("Update").Select(m => m.Name).ToList();
Require(updateCalls.IndexOf("UpdateMapLifecycle") >= 0 &&
    updateCalls.IndexOf("UpdateMapLifecycle") < updateCalls.IndexOf("UpdateVisualData"),
    "Deferred initialization must run before visual updates");
Require(LifecycleCalls("OnDestroy").Any(m => m.Name == "StopMapLifecycle"),
    "Destruction must cancel pending initialization");
foreach (var callback in new[] { "Start", "OnMapDidLoad", "OnMapWillUnload", "ReleaseMapLifecycle", "CleanupMap", "FindMapWindow" })
    Require(!LifecycleCalls(callback).Any(m => m.DeclaringType.FullName == "UI.Map.MapWindow" && m.Name == "get_instance"),
        callback + " must not use the unsafe map singleton");
Require(LifecycleCalls("FindMapWindow").Any(m => m.Name == "GetComponentInChildren"),
    "Map discovery must tolerate missing window registration");

// Verify every statically referenced game field/method, including members made
// accessible during compilation. This reads metadata; it never starts the game.
var gameScopes = new HashSet<string> { "Railroader", "Railroader.Modding", "Core", "Map.Runtime", "KeyValue.Runtime" };
int members = 0;
foreach (var method in allModTypes.SelectMany(t => t.Methods).Where(m => m.HasBody))
foreach (var instruction in method.Body.Instructions)
{
    if (instruction.Operand is not MemberReference member || member.DeclaringType == null) continue;
    var scope = member.DeclaringType.Scope?.Name?.Replace(".dll", "");
    if (scope == null || !gameScopes.Contains(scope)) continue;
    try
    {
        object? resolved = member switch { MethodReference m => m.Resolve(), FieldReference f => f.Resolve(), _ => member };
        Require(resolved != null, "Unresolved game member: " + member.FullName);
        members++;
    }
    catch (AssemblyResolutionException ex) { throw new Exception("Could not resolve " + member.FullName, ex); }
}

// The only IL rewrite replaces the two public zoom limits. Reject incompatible
// builds instead of relying on instruction offsets.
var zoom = allGameTypes["UI.Map.MapBuilder"].Methods.Single(m => m.Name == "Zoom");
foreach (float limit in new[] { 50f, 10000f })
    Require(zoom.Body.Instructions.Count(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == limit) == 1,
        "Zoom limit anchor changed: " + limit);

Require(allModTypes.Any(t => t.Interfaces.Any(i => i.InterfaceType.FullName == "Railroader.Modding.IModEntryPoint")),
    "Native entry point is missing");
Require(!mod.MainModule.AssemblyReferences.Any(r => r.Name.Contains("UnityModManager")), "Unexpected legacy loader dependency");
Require(!File.Exists(Path.ChangeExtension(modPath, ".pdb")), "Symbols must stay outside the distributable");

// The menu integration must stay optional even when its provider is not installed.
const string menuId = "crzbp/a-pretty-convenient-menu";
Require(!mod.MainModule.AssemblyReferences.Any(r => r.Name == "PrettyConvenientMenu"),
    "Optional menu must not become an assembly dependency");
Require(!File.Exists(Path.Combine(Path.GetDirectoryName(modPath)!, "PrettyConvenientMenu.dll")),
    "Optional menu must not be bundled");
using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(modPath)!, "Info.json"))))
{
    var dependencies = manifest.RootElement.GetProperty("dependencies");
    Require(!dependencies.GetProperty("required").EnumerateArray().Any(v => v.GetString()!.StartsWith(menuId)),
        "Menu provider must not be required");
    Require(dependencies.GetProperty("optional").EnumerateArray().Any(v => v.GetString() == menuId),
        "Menu provider must have optional load ordering");
}
if (args.Length == 3)
{
    using var provider = AssemblyDefinition.ReadAssembly(args[2], new ReaderParameters { AssemblyResolver = resolver });
    Require(provider.Name.Name == "PrettyConvenientMenu", "Unexpected optional provider assembly");
    var windowType = provider.MainModule.GetType("PrettyConvenientMenu.UI.Windows.IndustryProductionWindow");
    Require(windowType != null && windowType.IsPublic, "Optional provider window type changed");
    Require(windowType!.Methods.Any(m => m.Name == "Open" && m.IsPublic && m.IsStatic &&
        m.ReturnType.FullName == windowType.FullName && m.Parameters.Count == 1 &&
        m.Parameters[0].ParameterType.FullName == "Model.Ops.Industry"), "Optional provider Open contract changed");
    var hostType = provider.MainModule.GetType("PrettyConvenientMenu.Interaction.IndustryWorldInteraction");
    Require(hostType != null && hostType.IsPublic, "Optional provider world host changed");
    Require(hostType!.Properties.Any(p => p.Name == "Instance" && p.GetMethod is { IsStatic: true, IsPublic: true } &&
        p.PropertyType.FullName == hostType.FullName), "Optional provider readiness contract changed");
}


// Check the compiled destination-query control flow without loading game code.
bool Calls(Instruction i, string type, string name) =>
    i.Operand is MethodReference m && m.DeclaringType.FullName == type && m.Name == name;
IEnumerable<Instruction> NextInstructions(Instruction i)
{
    if (i.OpCode.FlowControl is FlowControl.Return or FlowControl.Throw) yield break;
    if (i.Operand is Instruction branch)
    {
        yield return branch;
        if (i.OpCode.FlowControl == FlowControl.Branch) yield break;
    }
    else if (i.Operand is Instruction[] branches)
        foreach (var target in branches) yield return target;
    if (i.Next != null) yield return i.Next;
}
bool CanReach(Instruction start, Instruction target, Instruction? stop = null,
    Instruction? blockedFrom = null, Instruction? blockedTo = null)
{
    var pending = new Stack<Instruction>();
    var visited = new HashSet<Instruction>();
    pending.Push(start);
    while (pending.Count > 0)
    {
        var current = pending.Pop();
        if (current == stop || !visited.Add(current)) continue;
        if (current == target) return true;
        foreach (var next in NextInstructions(current))
            if (current != blockedFrom || next != blockedTo) pending.Push(next);
    }
    return false;
}
int LocalNumber(Instruction i) => i.OpCode.Code switch
{
    Code.Ldloc_0 or Code.Stloc_0 => 0,
    Code.Ldloc_1 or Code.Stloc_1 => 1,
    Code.Ldloc_2 or Code.Stloc_2 => 2,
    Code.Ldloc_3 or Code.Stloc_3 => 3,
    Code.Ldloc or Code.Ldloc_S or Code.Stloc or Code.Stloc_S => ((VariableDefinition)i.Operand).Index,
    _ => -1
};
Instruction TrueBranch(Instruction branch) => branch.OpCode.Code switch
{
    Code.Brtrue or Code.Brtrue_S => (Instruction)branch.Operand,
    Code.Brfalse or Code.Brfalse_S => branch.Next,
    _ => throw new Exception("Expected a boolean guard branch")
};
Instruction FalseBranch(Instruction branch) => branch.OpCode.Code switch
{
    Code.Brfalse or Code.Brfalse_S => (Instruction)branch.Operand,
    Code.Brtrue or Code.Brtrue_S => branch.Next,
    _ => throw new Exception("Expected a boolean guard branch")
};
var destinationCalls = allModTypes.SelectMany(t => t.Methods).Where(m => m.HasBody)
    .SelectMany(m => m.Body.Instructions
        .Where(i => Calls(i, "Model.Ops.OpsControllerExtensions", "TryGetDestinationInfo"))
        .Select(i => (Method: m, Call: i))).ToList();
Require(destinationCalls.Count == 2, "Expected marker and freight-summary destination callers");
foreach (var (method, call) in destinationCalls)
{
    var guards = method.Body.Instructions
        .Where(i => Calls(i, "MapEnhancer.MapEnhancer", "CanQueryDestinationInfo")).ToList();
    Require(guards.Any(g => !CanReach(method.Body.Instructions[0], call,
        blockedFrom: g.Next, blockedTo: TrueBranch(g.Next))),
        "Destination query can bypass its integration guard: " + method.FullName);
}

var updater = allModTypes.Single(t => t.Name.StartsWith("<TraincarColorUpdater>"))
    .Methods.Single(m => m.Name == "MoveNext");
var offMap = updater.Body.Instructions.Single(i => Calls(i, "Model.Car", "get_IsInBardo"));
Require(offMap.Next.OpCode == OpCodes.Ldc_I4_0 && offMap.Next.Next.OpCode == OpCodes.Ceq,
    "Marker visibility must be the inverse of the off-map state");
var visibleLocal = LocalNumber(offMap.Next.Next.Next);
Require(visibleLocal >= 0, "Missing marker visibility local");
var visibilityCalls = updater.Body.Instructions.Where(i => i.Offset > offMap.Offset &&
    Calls(i, "UnityEngine.GameObject", "SetActive")).Take(2).ToList();
Require(visibilityCalls.Count == 2 && visibilityCalls.All(i => LocalNumber(i.Previous) == visibleLocal),
    "Both marker image and text must follow off-map visibility");
var visibilityBranch = visibilityCalls[1].Next.Next;
Require(LocalNumber(visibilityCalls[1].Next) == visibleLocal,
    "Off-map processing must branch on the visibility used for both marker elements");
var updaterQuery = destinationCalls.Single(c => c.Method == updater).Call;
var colorWrite = updater.Body.Instructions.Single(i => Calls(i, "UnityEngine.UI.Graphic", "set_color"));
Require(!CanReach(FalseBranch(visibilityBranch), updaterQuery, stop: offMap),
    "Hidden off-map marker can still query its destination in the current iteration");
Require(!CanReach(FalseBranch(visibilityBranch), colorWrite, stop: offMap),
    "Hidden off-map marker can still update its color in the current iteration");
Require(CanReach(TrueBranch(visibilityBranch), updaterQuery, stop: offMap),
    "Visible freight markers must retain destination updates");

// Evaluate only the compiled boolean return tail, never a game API or object.
bool BooleanTail(Instruction start, bool? input = null)
{
    var stack = new Stack<bool>();
    var locals = new Dictionary<int, bool>();
    if (input.HasValue) stack.Push(input.Value);
    var current = start;
    for (int step = 0; step < 32; step++)
    {
        switch (current.OpCode.Code)
        {
            case Code.Ldc_I4_0: stack.Push(false); break;
            case Code.Ldc_I4_1: stack.Push(true); break;
            case Code.Ceq: stack.Push(stack.Pop() == stack.Pop()); break;
            case Code.Stloc_0: case Code.Stloc_1: case Code.Stloc_2: case Code.Stloc_3:
            case Code.Stloc: case Code.Stloc_S: locals[LocalNumber(current)] = stack.Pop(); break;
            case Code.Ldloc_0: case Code.Ldloc_1: case Code.Ldloc_2: case Code.Ldloc_3:
            case Code.Ldloc: case Code.Ldloc_S: stack.Push(locals[LocalNumber(current)]); break;
            case Code.Br: case Code.Br_S: case Code.Leave: case Code.Leave_S:
                current = (Instruction)current.Operand; continue;
            case Code.Nop: break;
            case Code.Ret: return stack.Pop();
            default: throw new Exception("Unexpected destination guard return instruction: " + current);
        }
        current = current.Next;
    }
    throw new Exception("Destination guard return path did not terminate");
}
var destinationGate = allModTypes.Single(t => t.FullName == "MapEnhancer.MapEnhancer")
    .Methods.Single(m => m.Name == "CanQueryDestinationInfo");
var integrationRead = destinationGate.Body.Instructions.Single(i => Calls(i, "Model.Car", "get_set"));
var coupledCheck = destinationGate.Body.Instructions.Single(i =>
    Calls(i, "Model.Ops.CoupledToDestination", "IsCoupledToDestination"));
Require(BooleanTail(TrueBranch(integrationRead.Next)),
    "Cars with an integration set must retain native destination queries");
var noIntegration = FalseBranch(integrationRead.Next);
Require(CanReach(noIntegration, coupledCheck),
    "A car without an integration set must classify its destination before querying arrival");
Require(!destinationGate.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_I4_1 &&
    CanReach(noIntegration, i, stop: coupledCheck)),
    "A car without an integration set can bypass destination classification");
Require(!BooleanTail(coupledCheck.Next, true),
    "Coupling destinations without an integration set must not reach native arrival queries");
Require(BooleanTail(coupledCheck.Next, false),
    "Ordinary single-car destinations must remain queryable without an integration set");
Require(!allModTypes.SelectMany(t => t.Methods).Where(m => m.HasBody)
    .SelectMany(m => m.Body.Instructions).Any(i => Calls(i, "Model.Car", "EnumerateCoupled")),
    "A new direct coupling query needs its own integration guard");

var nativeLoader = allModTypes.Single(t => t.FullName == "MapEnhancer.Native.Loader");
Require(!allModTypes.Any(t => t.Namespace == "MapEnhancer.UMM"), "Legacy loader namespace remains.");
Require(allModTypes.Single(t => t.FullName == "MapEnhancer.Native.NativeEntryPoint").Interfaces
    .Any(i => i.InterfaceType.FullName == "Railroader.Modding.IModEntryPoint"),
    "Native entry point must implement the game loader contract.");
var cleanupMethod = nativeLoader.Methods.Single(m => m.Name == "CleanupNative");
Require(cleanupMethod.Body.Instructions.Count(i => Calls(i, "MapEnhancer.Native.NativeCleanup", "Run")) == 3,
    "Toolbar, patches and root destruction need independent cleanup boundaries.");
var loadMethod = nativeLoader.Methods.Single(m => m.Name == "LoadNative");
Require(loadMethod.Body.ExceptionHandlers.Where(h => h.HandlerType == ExceptionHandlerType.Catch).Any(h =>
{
    var body = loadMethod.Body.Instructions.Where(i => i.Offset >= h.HandlerStart.Offset &&
        (h.HandlerEnd == null || i.Offset < h.HandlerEnd.Offset)).ToList();
    return body.Any(i => Calls(i, "MapEnhancer.Native.Loader", "CleanupNative")) &&
        body.Any(i => i.OpCode == OpCodes.Rethrow) && !body.Any(i => i.OpCode == OpCodes.Throw);
}), "Failed load must run cleanup and rethrow its original exception.");
var unloadMethod = nativeLoader.Methods.Single(m => m.Name == "UnloadNative");
Require(unloadMethod.Body.ExceptionHandlers.Where(h => h.HandlerType == ExceptionHandlerType.Finally).Any(h =>
    unloadMethod.Body.Instructions.Where(i => i.Offset >= h.HandlerStart.Offset &&
        (h.HandlerEnd == null || i.Offset < h.HandlerEnd.Offset))
        .Any(i => Calls(i, "MapEnhancer.Native.Loader", "CleanupNative"))),
    "Unload must run the shared guarded cleanup in finally.");

// Boundary checks cover display choices, non-finite input, negative grade and
// all six public track classes. These tests have no game assembly dependency.
foreach (var sample in new[] {
    (0f, 0), (0.099f, 0), (0.1f, 1), (0.999f, 1), (1f, 2),
    (1.999f, 2), (2f, 3), (2.999f, 3), (3f, 4), (-3f, 4),
    (float.NaN, -1), (float.PositiveInfinity, -1) })
    Require(OverlayMath.GradeBand(sample.Item1) == sample.Item2, "Incorrect grade band");
foreach (var sample in new[] {
    (-1f, 0), (0f, 0), (0.001f, 1), (0.249f, 1), (0.25f, 2),
    (0.499f, 2), (0.5f, 3), (0.749f, 3), (0.75f, 4), (1f, 4),
    (2f, 4), (float.NaN, -1), (float.NegativeInfinity, -1) })
    Require(OverlayMath.UnitBand(sample.Item1) == sample.Item2, "Incorrect maintenance band");
for (int i = -2; i <= 7; i++)
    Require(OverlayMath.ClassBand(i) == (i >= 0 && i <= 5 ? i : -1), "Incorrect track class band");
Console.WriteLine($"PASS: {checks} assertions; {patches} Harmony patches; {members} game-member references; overlay boundaries; destination guards.");
Console.WriteLine("Offline compatibility checks do not establish in-game or multiplayer behavior.");
