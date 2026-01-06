using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public sealed class TestRunnerPlusWindow : EditorWindow, ICallbacks
{
    private const string MenuPath = "Tools/Tests/Test Runner+";

    [SerializeField] private TreeViewState _treeViewState;
    [SerializeField] private TestMode _testMode = TestMode.EditMode;
    [SerializeField] private bool _useDocSummaryAsDisplayName = true;
    [SerializeField] private bool _useNUnitDescriptionAsDisplayName = false;

    private TestRunnerApi _api;
    private SearchField _searchField;
    private string _searchText = string.Empty;
    private Vector2 _detailsScroll;

    private ITestAdaptor _root;
    private TestTreeView _treeView;
    private readonly TestSourceDocCache _docCache = new TestSourceDocCache();

    private bool _callbacksRegistered;
    private bool _runInProgress;
    private int _runTotalTestCases;
    private int _runCompletedTestCases;
    private int _runPassCount;
    private int _runFailCount;
    private int _runSkipCount;
    private int _runInconclusiveCount;
    private string _currentRunningTest;
    private DateTime _runStartedUtc;
    private DateTime _runFinishedUtc;
    private readonly Dictionary<string, TestResultInfo> _resultsByKey = new Dictionary<string, TestResultInfo>();

    private sealed class TestResultInfo
    {
        public TestStatus TestStatus;
        public string ResultState;
        public double Duration;
        public int AssertCount;
        public string Message;
        public string StackTrace;
        public string Output;
        public bool HasChildren;
    }

    [MenuItem(MenuPath)]
    public static void Open()
    {
        var window = GetWindow<TestRunnerPlusWindow>("Test Runner+");
        window.minSize = new Vector2(900f, 500f);
        window.Show();
    }

    private void OnEnable()
    {
        if (_treeViewState == null)
        {
            _treeViewState = new TreeViewState();
        }

        _api = CreateInstance<TestRunnerApi>();
        RegisterCallbacksIfNeeded();
        _treeView = new TestTreeView(_treeViewState, _docCache, TryGetResultInfo);
        _treeView.DisplayMode = new TestTreeView.DisplayNameMode(_useDocSummaryAsDisplayName, _useNUnitDescriptionAsDisplayName);

        _searchField = new SearchField();
        _searchField.downOrUpArrowKeyPressed += _treeView.SetFocusAndEnsureSelectedItem;

        RefreshTestList();
    }

    private void OnDisable()
    {
        UnregisterCallbacksIfNeeded();

        if (_api != null)
        {
            DestroyImmediate(_api);
            _api = null;
        }
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (_root == null)
        {
            EditorGUILayout.HelpBox("正在加载测试列表…（或当前模式下没有测试）", MessageType.Info);
            return;
        }

        var leftWidth = Mathf.Max(380f, position.width * 0.55f);

        using (new EditorGUILayout.HorizontalScope())
        {
            var leftRect = GUILayoutUtility.GetRect(leftWidth, leftWidth, 0f, 100000f, GUILayout.Width(leftWidth), GUILayout.ExpandHeight(true));

            _treeView.searchString = _searchText;
            _treeView.DisplayMode = new TestTreeView.DisplayNameMode(_useDocSummaryAsDisplayName, _useNUnitDescriptionAsDisplayName);
            _treeView.OnGUI(leftRect);

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                DrawDetailsLayout();
            }
        }

        DrawTooltipOverlay();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            var nextMode = DrawModeTabs(_testMode);
            if (nextMode != _testMode)
            {
                _testMode = nextMode;
                RefreshTestList();
            }

            GUILayout.Space(8);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                RefreshTestList();
            }

            GUILayout.Space(8);

            ITestAdaptor selectedForToolbar = null;
            var hasSelection = _treeView != null && _treeView.TryGetSingleSelection(out selectedForToolbar);

            using (new EditorGUI.DisabledScope(_runInProgress))
            {
                if (GUILayout.Button("Run All", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    RunAll();
                }

                using (new EditorGUI.DisabledScope(!hasSelection || selectedForToolbar == null))
                {
                    if (GUILayout.Button("Run Selected", EditorStyles.toolbarButton, GUILayout.Width(90)))
                    {
                        if (hasSelection && selectedForToolbar != null)
                        {
                            RunSelected(selectedForToolbar);
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Test Runner+", "请先在左侧选择一个测试节点。", "OK");
                        }
                    }
                }

                if (GUILayout.Button("Clear Results", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    ClearResults();
                }
            }

            GUILayout.Space(8);

            var prevDocAsName = _useDocSummaryAsDisplayName;
            var prevDescAsName = _useNUnitDescriptionAsDisplayName;

            _useDocSummaryAsDisplayName = GUILayout.Toggle(_useDocSummaryAsDisplayName, new GUIContent("Doc→Name", "用源码 XML Summary 作为显示名（更适合中文）"), EditorStyles.toolbarButton);
            _useNUnitDescriptionAsDisplayName = GUILayout.Toggle(_useNUnitDescriptionAsDisplayName, new GUIContent("Description→Name", "用 NUnit [Description] 作为显示名"), EditorStyles.toolbarButton);

            if (prevDocAsName != _useDocSummaryAsDisplayName || prevDescAsName != _useNUnitDescriptionAsDisplayName)
            {
                _treeView?.Reload();
            }

            GUILayout.Space(10);
            GUILayout.Label(new GUIContent(GetRunStatusText(), _currentRunningTest), EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            _searchText = _searchField.OnToolbarGUI(_searchText);
        }
    }

    private static TestMode DrawModeTabs(TestMode current)
    {
        var editOn = current == TestMode.EditMode;
        var playOn = current == TestMode.PlayMode;

        var nextEdit = GUILayout.Toggle(editOn, "EditMode", EditorStyles.toolbarButton, GUILayout.Width(80));
        var nextPlay = GUILayout.Toggle(playOn, "PlayMode", EditorStyles.toolbarButton, GUILayout.Width(80));

        if (nextEdit && !editOn) return TestMode.EditMode;
        if (nextPlay && !playOn) return TestMode.PlayMode;
        return current;
    }

    private string GetRunStatusText()
    {
        if (_runInProgress)
        {
            var totalText = _runTotalTestCases > 0 ? _runTotalTestCases.ToString() : "?";
            var completedText = _runTotalTestCases > 0 ? Mathf.Min(_runCompletedTestCases, _runTotalTestCases).ToString() : _runCompletedTestCases.ToString();
            return $"Running {completedText}/{totalText}  P{_runPassCount} F{_runFailCount} S{_runSkipCount} I{_runInconclusiveCount}";
        }

        if (_runPassCount > 0 || _runFailCount > 0 || _runSkipCount > 0 || _runInconclusiveCount > 0)
        {
            if (_runStartedUtc != default && _runFinishedUtc != default && _runFinishedUtc >= _runStartedUtc)
            {
                var seconds = (_runFinishedUtc - _runStartedUtc).TotalSeconds;
                return $"Last P{_runPassCount} F{_runFailCount} S{_runSkipCount} I{_runInconclusiveCount}  {seconds:0.###}s";
            }

            return $"Last P{_runPassCount} F{_runFailCount} S{_runSkipCount} I{_runInconclusiveCount}";
        }

        return "Idle";
    }

    private void DrawDetailsLayout()
    {
        var selected = _treeView.TryGetSingleSelection(out var test) ? test : null;
        if (selected == null)
        {
            EditorGUILayout.HelpBox("选择左侧任意测试节点以查看说明（支持从 XML Summary / NUnit Description 提取中文）。", MessageType.Info);
            return;
        }

        var doc = _docCache.GetDocInfo(selected);
        var displayName = _treeView.GetDisplayName(selected, doc);

        using (var scroll = new EditorGUILayout.ScrollViewScope(_detailsScroll))
        {
            _detailsScroll = scroll.scrollPosition;

            EditorGUILayout.LabelField("显示名", displayName);
            EditorGUILayout.LabelField("Name", selected.Name);
            EditorGUILayout.LabelField("FullName", selected.FullName);

            if (!string.IsNullOrWhiteSpace(selected.Description))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("NUnit Description", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(selected.Description, EditorStyles.textArea, GUILayout.MinHeight(36));
            }

            if (!string.IsNullOrWhiteSpace(doc.Summary))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("XML Summary（源码注释）", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(doc.Summary, EditorStyles.textArea, GUILayout.MinHeight(36));
            }

            if (selected.Categories != null && selected.Categories.Length > 0)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Categories", string.Join(", ", selected.Categories));
            }

            if (!string.IsNullOrWhiteSpace(doc.AssetPath))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel($"{doc.AssetPath}:{doc.LineNumber}", EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }

            var resultInfo = TryGetResultInfo(selected);
            if (resultInfo != null)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Last Result", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Status", $"{resultInfo.TestStatus} ({resultInfo.ResultState})");
                EditorGUILayout.LabelField("Duration", $"{resultInfo.Duration:0.###}s");
                EditorGUILayout.LabelField("Asserts", resultInfo.AssertCount.ToString());

                if (!string.IsNullOrWhiteSpace(resultInfo.Message))
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Message", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(resultInfo.Message, EditorStyles.textArea, GUILayout.MinHeight(36));
                }

                if (!string.IsNullOrWhiteSpace(resultInfo.StackTrace))
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("StackTrace", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(resultInfo.StackTrace, EditorStyles.textArea, GUILayout.MinHeight(72));
                }

                if (!string.IsNullOrWhiteSpace(resultInfo.Output))
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(resultInfo.Output, EditorStyles.textArea, GUILayout.MinHeight(36));
                }
            }
        }

        GUILayout.Space(6);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_runInProgress))
            {
                if (GUILayout.Button(new GUIContent("Run Selected", "运行当前选中节点（若是 Suite 会递归执行其下所有用例）"), GUILayout.Height(26)))
                {
                    RunSelected(selected);
                }
            }

            if (GUILayout.Button(new GUIContent("Copy FullName", "复制 FullName（可用于 Filter.testNames）"), GUILayout.Height(26)))
            {
                EditorGUIUtility.systemCopyBuffer = selected.FullName ?? string.Empty;
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(doc.AssetPath)))
            {
                if (GUILayout.Button(new GUIContent("Open Source", "打开源码定位到对应行"), GUILayout.Height(26)))
                {
                    OpenSource(doc);
                }
            }
        }
    }

    private void DrawTooltipOverlay()
    {
        if (string.IsNullOrWhiteSpace(GUI.tooltip))
        {
            return;
        }

        var rect = new Rect(8f, position.height - 56f, position.width - 16f, 48f);
        GUI.Label(rect, GUI.tooltip, EditorStyles.helpBox);
        Repaint();
    }

    private void RefreshTestList()
    {
        _root = null;
        _treeView?.Clear();
        _docCache.Clear();

        if (_api == null)
        {
            _api = CreateInstance<TestRunnerApi>();
        }

        RegisterCallbacksIfNeeded();

        _api.RetrieveTestList(_testMode, testRoot =>
        {
            _root = testRoot;
            _treeView.DisplayMode = new TestTreeView.DisplayNameMode(_useDocSummaryAsDisplayName, _useNUnitDescriptionAsDisplayName);
            _treeView.SetRoot(testRoot);
            Repaint();
        });
    }

    private void RegisterCallbacksIfNeeded()
    {
        if (_callbacksRegistered)
        {
            return;
        }

        if (_api == null)
        {
            _api = CreateInstance<TestRunnerApi>();
        }

        _api.RegisterCallbacks(this);
        _callbacksRegistered = true;
    }

    private void UnregisterCallbacksIfNeeded()
    {
        if (!_callbacksRegistered)
        {
            return;
        }

        if (_api == null)
        {
            _api = CreateInstance<TestRunnerApi>();
        }

        try
        {
            _api.UnregisterCallbacks(this);
        }
        catch
        {
            // Best effort.
        }
        finally
        {
            _callbacksRegistered = false;
        }
    }

    private TestResultInfo TryGetResultInfo(ITestAdaptor test)
    {
        var key = GetLookupKey(test);
        return !string.IsNullOrWhiteSpace(key) && _resultsByKey.TryGetValue(key, out var info) ? info : null;
    }

    private static string GetLookupKey(ITestAdaptor test)
    {
        if (test == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(test.UniqueName))
        {
            return test.UniqueName;
        }

        if (!string.IsNullOrWhiteSpace(test.FullName))
        {
            return test.FullName;
        }

        return test.Name ?? string.Empty;
    }

    private static string GetLookupKey(ITestResultAdaptor result)
    {
        if (result == null)
        {
            return string.Empty;
        }

        var testKey = GetLookupKey(result.Test);
        if (!string.IsNullOrWhiteSpace(testKey))
        {
            return testKey;
        }

        if (!string.IsNullOrWhiteSpace(result.FullName))
        {
            return result.FullName;
        }

        return result.Name ?? string.Empty;
    }

    private void RunSelected(ITestAdaptor selected)
    {
        if (_api == null || selected == null)
        {
            return;
        }

        if (EditorUtility.scriptCompilationFailed)
        {
            Debug.LogError("Fix compilation issues before running tests");
            return;
        }

        if (_runInProgress)
        {
            EditorUtility.DisplayDialog("Test Runner+", "当前已有测试在运行中。", "OK");
            return;
        }

        var testNames = new HashSet<string>();
        CollectLeafTestFullNames(selected, testNames);

        if (testNames.Count == 0)
        {
            EditorUtility.DisplayDialog("Test Runner+", "当前选择没有可执行的测试用例。", "OK");
            return;
        }

        var filter = new Filter
        {
            testMode = _testMode,
            testNames = testNames.ToArray(),
        };

        PrepareForNewRun(selected.TestCaseCount);
        try
        {
            _api.Execute(new ExecutionSettings(filter));
        }
        catch (Exception ex)
        {
            _runInProgress = false;
            Debug.LogException(ex);
            Repaint();
        }
    }

    private void RunAll()
    {
        if (_api == null)
        {
            return;
        }

        if (EditorUtility.scriptCompilationFailed)
        {
            Debug.LogError("Fix compilation issues before running tests");
            return;
        }

        if (_runInProgress)
        {
            EditorUtility.DisplayDialog("Test Runner+", "当前已有测试在运行中。", "OK");
            return;
        }

        PrepareForNewRun(_root != null ? _root.TestCaseCount : 0);
        try
        {
            _api.Execute(new ExecutionSettings(new Filter { testMode = _testMode }));
        }
        catch (Exception ex)
        {
            _runInProgress = false;
            Debug.LogException(ex);
            Repaint();
        }
    }

    private void PrepareForNewRun(int expectedTotalCases)
    {
        _runInProgress = true;
        _runStartedUtc = DateTime.UtcNow;
        _runFinishedUtc = default;
        _runTotalTestCases = Mathf.Max(0, expectedTotalCases);
        _runCompletedTestCases = 0;
        _runPassCount = 0;
        _runFailCount = 0;
        _runSkipCount = 0;
        _runInconclusiveCount = 0;
        _currentRunningTest = null;
        _resultsByKey.Clear();

        _treeView?.Reload();
        Repaint();
    }

    private void ClearResults()
    {
        _runInProgress = false;
        _runTotalTestCases = 0;
        _runCompletedTestCases = 0;
        _runPassCount = 0;
        _runFailCount = 0;
        _runSkipCount = 0;
        _runInconclusiveCount = 0;
        _currentRunningTest = null;
        _runStartedUtc = default;
        _runFinishedUtc = default;
        _resultsByKey.Clear();

        _treeView?.Reload();
        Repaint();
    }

    public void RunStarted(ITestAdaptor testsToRun)
    {
        PrepareForNewRun(testsToRun != null ? testsToRun.TestCaseCount : _runTotalTestCases);
    }

    public void RunFinished(ITestResultAdaptor result)
    {
        _runInProgress = false;
        _runFinishedUtc = DateTime.UtcNow;
        _currentRunningTest = null;

        if (result != null)
        {
            _runPassCount = result.PassCount;
            _runFailCount = result.FailCount;
            _runSkipCount = result.SkipCount;
            _runInconclusiveCount = result.InconclusiveCount;

            var total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
            _runTotalTestCases = Mathf.Max(_runTotalTestCases, total);
            _runCompletedTestCases = Mathf.Max(_runCompletedTestCases, total);

            _resultsByKey.Clear();
            CacheResultRecursive(result);
        }

        _treeView?.Reload();
        Repaint();
    }

    public void TestStarted(ITestAdaptor test)
    {
        _currentRunningTest = test != null ? (test.FullName ?? test.Name) : null;
        Repaint();
    }

    public void TestFinished(ITestResultAdaptor result)
    {
        if (result == null)
        {
            return;
        }

        var key = GetLookupKey(result);
        if (!string.IsNullOrWhiteSpace(key))
        {
            _resultsByKey[key] = ToResultInfo(result);
        }

        if (!result.HasChildren)
        {
            _runCompletedTestCases++;

            switch (result.TestStatus)
            {
                case TestStatus.Passed:
                    _runPassCount++;
                    break;
                case TestStatus.Failed:
                    _runFailCount++;
                    break;
                case TestStatus.Skipped:
                    _runSkipCount++;
                    break;
                case TestStatus.Inconclusive:
                    _runInconclusiveCount++;
                    break;
            }
        }

        Repaint();
    }

    private void CacheResultRecursive(ITestResultAdaptor result)
    {
        if (result == null)
        {
            return;
        }

        var key = GetLookupKey(result);
        if (!string.IsNullOrWhiteSpace(key))
        {
            _resultsByKey[key] = ToResultInfo(result);
        }

        if (!result.HasChildren)
        {
            return;
        }

        foreach (var child in result.Children)
        {
            CacheResultRecursive(child);
        }
    }

    private static TestResultInfo ToResultInfo(ITestResultAdaptor result)
    {
        return new TestResultInfo
        {
            TestStatus = result.TestStatus,
            ResultState = result.ResultState,
            Duration = result.Duration,
            AssertCount = result.AssertCount,
            Message = result.Message,
            StackTrace = result.StackTrace,
            Output = result.Output,
            HasChildren = result.HasChildren,
        };
    }

    private static void CollectLeafTestFullNames(ITestAdaptor node, HashSet<string> output)
    {
        if (node == null)
        {
            return;
        }

        if (!node.HasChildren)
        {
            if (!string.IsNullOrWhiteSpace(node.FullName))
            {
                output.Add(node.FullName);
            }

            return;
        }

        foreach (var child in node.Children)
        {
            CollectLeafTestFullNames(child, output);
        }
    }

    private static void OpenSource(TestSourceDocCache.DocInfo doc)
    {
        if (doc == null || string.IsNullOrWhiteSpace(doc.AssetPath))
        {
            return;
        }

        var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(doc.AssetPath);
        if (asset == null)
        {
            return;
        }

        var line = Mathf.Max(1, doc.LineNumber);
        AssetDatabase.OpenAsset(asset, line);
    }

    private sealed class TestTreeView : TreeView
    {
        public readonly struct DisplayNameMode
        {
            public readonly bool UseDocSummary;
            public readonly bool UseNUnitDescription;

            public DisplayNameMode(bool useDocSummary, bool useNUnitDescription)
            {
                UseDocSummary = useDocSummary;
                UseNUnitDescription = useNUnitDescription;
            }
        }

        private sealed class Item : TreeViewItem
        {
            public ITestAdaptor Test;
        }

        private static class ResultIcons
        {
            public static readonly Texture2D Fail;
            public static readonly Texture2D Ignore;
            public static readonly Texture2D Success;
            public static readonly Texture2D Inconclusive;

            static ResultIcons()
            {
                Fail = EditorGUIUtility.IconContent("TestFailed").image as Texture2D;
                Ignore = EditorGUIUtility.IconContent("TestIgnored").image as Texture2D;
                Success = EditorGUIUtility.IconContent("TestPassed").image as Texture2D;
                Inconclusive = EditorGUIUtility.IconContent("TestInconclusive").image as Texture2D;
            }
        }

        private ITestAdaptor _root;
        private readonly TestSourceDocCache _docCache;
        private readonly Func<ITestAdaptor, TestResultInfo> _resultLookup;
        private readonly Dictionary<int, ITestAdaptor> _byId = new Dictionary<int, ITestAdaptor>();

        public DisplayNameMode DisplayMode { get; set; }

        public TestTreeView(TreeViewState state, TestSourceDocCache docCache, Func<ITestAdaptor, TestResultInfo> resultLookup)
            : base(state)
        {
            _docCache = docCache;
            _resultLookup = resultLookup;
            rowHeight = EditorGUIUtility.singleLineHeight + 2f;
            showBorder = true;
            Reload();
        }

        public void SetRoot(ITestAdaptor root)
        {
            _root = root;
            Reload();
        }

        public void Clear()
        {
            _root = null;
            _byId.Clear();
            Reload();
        }

        public bool TryGetSingleSelection(out ITestAdaptor test)
        {
            test = null;
            var selection = GetSelection();
            if (selection == null || selection.Count != 1)
            {
                return false;
            }

            return _byId.TryGetValue(selection[0], out test) && test != null;
        }

        public string GetDisplayName(ITestAdaptor test, TestSourceDocCache.DocInfo doc)
        {
            if (test == null)
            {
                return string.Empty;
            }

            if (DisplayMode.UseDocSummary && !string.IsNullOrWhiteSpace(doc?.Summary))
            {
                return doc.Summary;
            }

            if (DisplayMode.UseNUnitDescription && !string.IsNullOrWhiteSpace(test.Description))
            {
                return NormalizeOneLine(test.Description);
            }

            return test.Name ?? string.Empty;
        }

        protected override TreeViewItem BuildRoot()
        {
            _byId.Clear();

            var rootItem = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };

            if (_root == null)
            {
                rootItem.children = new List<TreeViewItem>();
                return rootItem;
            }

            var built = BuildItemRecursive(_root, 0);
            rootItem.AddChild(built);

            SetupDepthsFromParentsAndChildren(rootItem);
            return rootItem;
        }

        private Item BuildItemRecursive(ITestAdaptor test, int depth)
        {
            var id = GetStableIdFor(test);
            _byId[id] = test;

            var doc = _docCache.GetDocInfo(test);
            var displayName = GetDisplayName(test, doc);
            var resultInfo = _resultLookup != null ? _resultLookup(test) : null;

            var item = new Item
            {
                id = id,
                depth = depth,
                displayName = displayName,
                Test = test,
                icon = GetIconForResult(resultInfo),
            };

            if (test.HasChildren)
            {
                item.children = new List<TreeViewItem>();
                foreach (var child in test.Children)
                {
                    item.AddChild(BuildItemRecursive(child, depth + 1));
                }
            }

            return item;
        }

        private int GetStableIdFor(ITestAdaptor test)
        {
            var key = GetLookupKey(test);
            var id = Animator.StringToHash(key);
            if (id == 0)
            {
                id = 1;
            }

            while (_byId.ContainsKey(id))
            {
                id = unchecked(id + 1);
                if (id == 0)
                {
                    id = 1;
                }
            }

            return id;
        }

        private static Texture2D GetIconForResult(TestResultInfo info)
        {
            if (info == null)
            {
                return null;
            }

            switch (info.TestStatus)
            {
                case TestStatus.Passed:
                    return ResultIcons.Success;
                case TestStatus.Failed:
                    return ResultIcons.Fail;
                case TestStatus.Skipped:
                    return ResultIcons.Ignore;
                case TestStatus.Inconclusive:
                    return ResultIcons.Inconclusive;
                default:
                    return null;
            }
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (!(args.item is Item item) || item.Test == null)
            {
                base.RowGUI(args);
                return;
            }

            base.RowGUI(args);

            var doc = _docCache.GetDocInfo(item.Test);
            var resultInfo = _resultLookup != null ? _resultLookup(item.Test) : null;
            var tooltip = BuildTooltip(item.Test, doc, resultInfo);
            GUI.Label(args.rowRect, new GUIContent(string.Empty, tooltip), GUIStyle.none);
        }

        private static string BuildTooltip(ITestAdaptor test, TestSourceDocCache.DocInfo doc, TestResultInfo resultInfo)
        {
            var parts = new List<string>();

            if (resultInfo != null)
            {
                parts.Add($"Result: {resultInfo.TestStatus} ({resultInfo.ResultState})");
            }

            if (!string.IsNullOrWhiteSpace(doc?.Summary))
            {
                parts.Add(doc.Summary);
            }

            if (!string.IsNullOrWhiteSpace(test.Description))
            {
                var desc = NormalizeOneLine(test.Description);
                if (!parts.Contains(desc))
                {
                    parts.Add(desc);
                }
            }

            if (!string.IsNullOrWhiteSpace(test.FullName))
            {
                parts.Add(test.FullName);
            }

            if (!string.IsNullOrWhiteSpace(doc?.AssetPath))
            {
                parts.Add($"{doc.AssetPath}:{doc.LineNumber}");
            }

            return string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static string NormalizeOneLine(string text)
        {
            return string.Join(" ", text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }
    }

    private sealed class TestSourceDocCache
    {
        public sealed class DocInfo
        {
            public string Summary;
            public string AssetPath;
            public int LineNumber;
        }

        private readonly Dictionary<string, DocInfo> _docByKey = new Dictionary<string, DocInfo>();
        private readonly Dictionary<Type, string> _scriptPathByType = new Dictionary<Type, string>();
        private readonly Dictionary<string, string[]> _linesByPath = new Dictionary<string, string[]>();

        public void Clear()
        {
            _docByKey.Clear();
            _scriptPathByType.Clear();
            _linesByPath.Clear();
        }

        public DocInfo GetDocInfo(ITestAdaptor test)
        {
            if (test == null)
            {
                return new DocInfo();
            }

            var key = BuildCacheKey(test);
            if (_docByKey.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var doc = new DocInfo();
            TryFillDocInfo(test, doc);
            _docByKey[key] = doc;
            return doc;
        }

        private static string BuildCacheKey(ITestAdaptor test)
        {
            if (test.Method != null && test.Method.MethodInfo != null)
            {
                var method = test.Method.MethodInfo;
                var typeName = method.DeclaringType != null ? method.DeclaringType.FullName : "<unknown-type>";
                return $"{typeName}.{method.Name}";
            }

            if (test.TypeInfo != null && test.TypeInfo.Type != null)
            {
                return test.TypeInfo.Type.FullName ?? test.TypeInfo.Type.Name;
            }

            return test.UniqueName ?? test.FullName ?? test.Name ?? string.Empty;
        }

        private void TryFillDocInfo(ITestAdaptor test, DocInfo doc)
        {
            if (test.Method != null && test.Method.MethodInfo != null)
            {
                var method = test.Method.MethodInfo;
                var declaringType = method.DeclaringType;
                if (declaringType == null)
                {
                    return;
                }

                if (!TryGetScriptPath(declaringType, out var scriptPath))
                {
                    return;
                }

                if (!TryGetLines(scriptPath, out var lines))
                {
                    return;
                }

                var lineIndex = FindMemberLineIndex(lines, BuildMethodLineRegex(method.Name));
                doc.AssetPath = scriptPath;
                doc.LineNumber = lineIndex >= 0 ? lineIndex + 1 : 1;
                doc.Summary = ExtractSummaryBeforeLine(lines, lineIndex);
                return;
            }

            if (test.TypeInfo != null && test.TypeInfo.Type != null)
            {
                var type = test.TypeInfo.Type;

                if (!TryGetScriptPath(type, out var scriptPath))
                {
                    return;
                }

                if (!TryGetLines(scriptPath, out var lines))
                {
                    return;
                }

                var lineIndex = FindMemberLineIndex(lines, BuildClassLineRegex(type.Name));
                doc.AssetPath = scriptPath;
                doc.LineNumber = lineIndex >= 0 ? lineIndex + 1 : 1;
                doc.Summary = ExtractSummaryBeforeLine(lines, lineIndex);
            }
        }

        private bool TryGetScriptPath(Type type, out string scriptPath)
        {
            if (type == null)
            {
                scriptPath = null;
                return false;
            }

            if (_scriptPathByType.TryGetValue(type, out scriptPath))
            {
                return !string.IsNullOrWhiteSpace(scriptPath);
            }

            scriptPath = FindMonoScriptPath(type, new[] { "Assets/Tests", "Assets/Scripts" })
                         ?? FindMonoScriptPath(type, new[] { "Assets" });

            _scriptPathByType[type] = scriptPath;
            return !string.IsNullOrWhiteSpace(scriptPath);
        }

        private static string FindMonoScriptPath(Type type, string[] folders)
        {
            var guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript", folders);
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null)
                {
                    continue;
                }

                if (script.GetClass() == type)
                {
                    return path;
                }
            }

            return null;
        }

        private bool TryGetLines(string assetPath, out string[] lines)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                lines = null;
                return false;
            }

            if (_linesByPath.TryGetValue(assetPath, out lines))
            {
                return lines != null;
            }

            try
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                var fullPath = Path.Combine(projectRoot ?? string.Empty, assetPath);
                lines = File.ReadAllLines(fullPath);
            }
            catch
            {
                lines = null;
            }

            _linesByPath[assetPath] = lines;
            return lines != null;
        }

        private static Regex BuildClassLineRegex(string typeName)
        {
            return new Regex($@"\bclass\s+{Regex.Escape(typeName)}\b", RegexOptions.Compiled);
        }

        private static Regex BuildMethodLineRegex(string methodName)
        {
            return new Regex($@"\b{Regex.Escape(methodName)}\s*\(", RegexOptions.Compiled);
        }

        private static int FindMemberLineIndex(string[] lines, Regex memberRegex)
        {
            if (lines == null || memberRegex == null)
            {
                return -1;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                if (memberRegex.IsMatch(lines[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string ExtractSummaryBeforeLine(string[] lines, int memberLineIndex)
        {
            if (lines == null || memberLineIndex <= 0)
            {
                return null;
            }

            var i = memberLineIndex - 1;
            while (i >= 0)
            {
                var trimmed = lines[i].Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("["))
                {
                    i--;
                    continue;
                }

                break;
            }

            if (i < 0 || !lines[i].TrimStart().StartsWith("///"))
            {
                return null;
            }

            var docLines = new List<string>();
            while (i >= 0 && lines[i].TrimStart().StartsWith("///"))
            {
                var line = lines[i].TrimStart();
                line = line.Length >= 3 ? line.Substring(3) : string.Empty;
                docLines.Add(line.TrimStart());
                i--;
            }

            docLines.Reverse();
            var xml = string.Join("\n", docLines);

            var match = Regex.Match(xml, "<summary>(.*?)</summary>", RegexOptions.Singleline);
            var summary = match.Success ? match.Groups[1].Value : xml;

            summary = Regex.Replace(summary, "<.*?>", string.Empty, RegexOptions.Singleline);
            summary = NormalizeWhitespace(summary);
            return string.IsNullOrWhiteSpace(summary) ? null : summary;
        }

        private static string NormalizeWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return string.Join(" ", text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }
    }
}
