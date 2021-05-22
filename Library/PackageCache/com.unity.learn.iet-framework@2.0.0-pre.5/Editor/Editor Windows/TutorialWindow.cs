using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.EditorCoroutines.Editor;

using UnityObject = UnityEngine.Object;
using static Unity.Tutorials.Core.Editor.RichTextParser;
using UnityEditor.SceneManagement;

namespace Unity.Tutorials.Core.Editor
{
    using static Localization;

    /// <summary>
    /// The window used to display all tutorial content.
    /// </summary>
    public sealed class TutorialWindow : EditorWindowProxy
    {
        /// <summary>
        /// Should we show the Close Tutorials info dialog for the user for the current project.
        /// By default the dialog is shown once per project and disabled after that.
        /// </summary>
        /// <remarks>
        /// You want to set this typically to false when running unit tests.
        /// </remarks>
        public static ProjectSetting<bool> ShowTutorialsClosedDialog =
            new ProjectSetting<bool>("IET.ShowTutorialsClosedDialog", "Show info dialog when the window is closed", true);

        VisualElement m_VideoBoxElement;

        const int k_MinWidth = 300;
        const int k_MinHeight = 300;

        SystemLanguage m_CurrentEditorLanguage; // uninitialized in order to force translation when the window is enabled for the first time

        List<TutorialParagraph> m_Paragraphs = new List<TutorialParagraph>();
        int[] m_Indexes;
        [SerializeField]
        List<TutorialParagraph> m_AllParagraphs = new List<TutorialParagraph>();

        static readonly bool s_AuthoringMode = ProjectMode.IsAuthoringMode();

        string m_NextButtonText = "";
        string m_BackButtonText = "";
        string m_WindowTitleContent;
        string m_PromptOk;
        string m_MenuPathGuide;
        string m_TabClosedDialogTitle;
        string m_TabClosedDialogText;
        string m_SelectTutorialsText;

        bool m_IsInitialized;

        enum State { ContainerSelection, TutorialSelection, TutorialInProgress }

        class Card
        {
            public VisualElement Element { get; set; }
            public object Target { get; set; }

            public virtual string Heading { get; }
            public virtual string Text { get; }
            public virtual string Tooltip { get; }
            public virtual Texture2D Image { get; }
        }

        class ContainerCard : Card
        {
            public TutorialContainer Container => Target as TutorialContainer;

            public override string Heading => Container.Title;
            public override string Text => Container.Subtitle;
            public override string Tooltip => Container.Description;
            public override Texture2D Image => Container.BackgroundImage;
        }

        class SectionCard : Card
        {
            public TutorialContainer.Section Section => Target as TutorialContainer.Section;

            public override string Heading => Section.Heading;
            public override string Text => Section.Text;
            public override Texture2D Image => Section.Image;
        }

        class TutorialCard : SectionCard
        {
            public override string Tooltip => Tr("Tutorial: ") + Section.Text;
        }

        class LinkCard : SectionCard
        {
            public override string Tooltip => Section.Url;
        }

        // TODO rename to CurrentTutorial (will cause a big delta, not doing right now) and make public
        internal Tutorial currentTutorial
        {
            get
            {
                return m_CurrentTutorial;
            }
            set
            {
                m_CurrentTutorial?.Modified?.RemoveListener(OnTutorialModified);
                m_CurrentTutorial = value;
                if (m_CurrentTutorial != null)
                    m_CurrentTutorial.Modified.AddListener(OnTutorialModified);
            }
        }
        Tutorial m_CurrentTutorial;

        /// <summary>
        /// Creates the window if it does not exist, anchoring it as a tab next to the first found Inspector.
        /// If the window exists already, it's simply brought to the foreground and focused without any other actions.
        /// If any Inspector is not visible currently, Tutorials window is will be shown as a free-floating window.
        /// </summary>
        /// <remarks>
        /// This is the new and preferred way to show the Tutorials window.
        /// </remarks>
        /// <returns></returns>
        internal static TutorialWindow CreateNextToInspector()
        {
            var inspectorWindow = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .FirstOrDefault(wnd => wnd.GetType().Name == "InspectorWindow");

            Type windowToAnchorTo = inspectorWindow != null ? inspectorWindow.GetType() : null;
            bool alreadyCreated = EditorWindowUtils.FindOpenInstance<TutorialWindow>() != null;
            // If Inspector not visible/opened, Tutorials window will be created as a free-floating window
            var tutorialWindow = GetOrCreateWindow(windowToAnchorTo); // create & anchor or simply focus
            if (alreadyCreated)
                return tutorialWindow;

            if (inspectorWindow)
                inspectorWindow.DockWindow(tutorialWindow, EditorWindowUtils.DockPosition.Right);

            return tutorialWindow;
        }

        /// <summary>
        /// Creates the window if it does not exist, and positions it using a window layout
        /// specified either by the TutorialContainer.ProjectLayout or Tutorial Framework's default layout.
        /// If the window exists already, it's simply brought to the foreground and focused without any other actions.
        /// If the project layout does not contain Tutorials window, it will be shown as a free-floating window.
        /// </summary>
        /// <remarks>
        /// This is the old way to show the Tutorials window and should be preferred only in situations where
        /// a special window layout is preferred when starting a tutorial project for the first time.
        /// </remarks>
        /// <param name="container">The container used for the project layout setting.</param>
        /// <returns></returns>
        internal static TutorialWindow CreateWindowAndLoadLayout(TutorialContainer container)
        {
            var tutorialWindow = EditorWindowUtils.FindOpenInstance<TutorialWindow>();
            if (tutorialWindow != null)
                return GetOrCreateWindow(); // focus

            if (container != null)
                container.LoadTutorialProjectLayout();

            // If project layout did not contain tutorial window, it will be created as a free-floating window
            tutorialWindow = EditorWindowUtils.FindOpenInstance<TutorialWindow>();
            if (tutorialWindow == null)
                tutorialWindow = GetOrCreateWindow(); // create

            return tutorialWindow;
        }

        /// <summary>
        /// Creates a window and positions it as a tab of another window, if wanted.
        /// If the window exists already, it's brought to the foreground and focused.
        /// </summary>
        /// <param name="windowToAnchorTo"></param>
        /// <returns></returns>
        internal static TutorialWindow GetOrCreateWindow(Type windowToAnchorTo = null)
        {
            var window = GetWindow<TutorialWindow>(windowToAnchorTo);
            window.minSize = new Vector2(k_MinWidth, k_MinHeight);
            window.titleContent.text = Tr("Tutorials");
            return window;
        }

        /// <summary>
        /// Active container of which tutorials we are viewing.
        /// </summary>
        /// <remarks>
        /// </remarks>
        public TutorialContainer ActiveContainer
        {
            get { return m_ActiveContainer; }
            set
            {
                m_ActiveContainer = value;
                InitializeUI();
            }
        }
        [SerializeField]
        TutorialContainer m_ActiveContainer;

        /// <summary>
        /// Sets the containers, "tutorial projects", available for selection.
        /// </summary>
        /// <param name="containers">Container selection.</param>
        /// <remarks>
        /// ActiveContainer must be set to null in order to view the selection.
        /// </remarks>
        public void SetContainers(IEnumerable<TutorialContainer> containers)
        {
            ClearContainers();
            foreach(var container in containers)
            {
                if (!m_Containers.Contains(container))
                    m_Containers.Add(container);
            }
        }

        /// <summary>
        /// Clears any containers the window might be showing as available.
        /// </summary>
        /// <remarks>
        /// If we were viewing the container selection, the window is cleared.
        /// </remarks>
        public void ClearContainers()
        {
            m_Containers.ForEach(UnsubscribeFromContainer);
            m_Containers.Clear();
            InitializeUI();
        }

        [SerializeField]
        List<TutorialContainer> m_Containers = new List<TutorialContainer>();

        [SerializeField]
        Card[] m_Cards = { };

        bool CanMoveToNextPage =>
            currentTutorial != null && currentTutorial.CurrentPage != null &&
            (currentTutorial.CurrentPage.AreAllCriteriaSatisfied ||
                currentTutorial.CurrentPage.HasMovedToNextPage);

        bool MaskingEnabled
        {
            get
            {
                return MaskingManager.MaskingEnabled && (m_MaskingEnabled || !s_AuthoringMode);
            }
            set { m_MaskingEnabled = value; }
        }
        [SerializeField]
        bool m_MaskingEnabled = true;

        TutorialStyles Styles { get { return TutorialProjectSettings.Instance.TutorialStyle; } }

        [SerializeField]
        int m_FarthestPageCompleted = -1;

        [SerializeField]
        bool m_PlayModeChanging;

        VideoPlaybackManager VideoPlaybackManager { get; } = new VideoPlaybackManager();
        Texture m_VideoTextureCache;

        bool m_DoneFetchingTutorialStates = false;

        double m_CheckLanguageTick = 0f;
        double m_BlinkTick = 0f;
        bool m_BlinkOn = true;

        double m_EditorDeltaTime = 0f;
        double m_LastTimeSinceStartup = 0f;
        EditorCoroutine m_WaitForStylesRoutine;

        void SubscribeToContainer(TutorialContainer container)
        {
            container.Modified.AddListener(OnTutorialContainerModified);
        }

        void UnsubscribeFromContainer(TutorialContainer container)
        {
            container.Modified.RemoveListener(OnTutorialContainerModified);
        }

        void OnTutorialContainerModified(TutorialContainer container)
        {
            Debug.Assert(m_Containers.Contains(container) || ActiveContainer == container);

            if (currentTutorial == null)
                InitializeUI();
        }

        void TrackPlayModeChanging(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                    m_PlayModeChanging = true;
                    break;
                case PlayModeStateChange.EnteredEditMode:
                case PlayModeStateChange.EnteredPlayMode:
                    m_PlayModeChanging = false;
                    break;
            }
        }

        void UpdateVideoFrame(Texture newTexture)
        {
            rootVisualElement.Q("TutorialMedia").style.backgroundImage = Background.FromTexture2D((Texture2D)newTexture);
        }

        void UpdateHeader(TextElement contextText, TextElement titleText, VisualElement backDrop)
        {
            bool hasTutorial = currentTutorial != null;
            var context = ActiveContainer != null ? ActiveContainer.Subtitle.Value : string.Empty;
            var title = hasTutorial ? currentTutorial.TutorialTitle.Value : ActiveContainer?.Title.Value;
            // For now drawing header only for Readme
            if (ActiveContainer)
            {
                contextText.text = context;
                titleText.text = title;

                backDrop.style.backgroundImage = ActiveContainer.BackgroundImage;
            }
        }

        void ScrollToTop()
        {
            ((ScrollView)rootVisualElement.Q("TutorialContainer").ElementAt(0)).scrollOffset = Vector2.zero;
        }

        void ShowCurrentTutorialContent()
        {
            if (!m_AllParagraphs.Any() || !currentTutorial)
                return;
            if (m_AllParagraphs.Count() <= currentTutorial.CurrentPageIndex)
                return;

            SetWindowState(State.TutorialInProgress);
            ScrollToTop();

            TutorialParagraph instruction = null;
            TutorialParagraph narrative = null;
            Tutorial linkedTutorial = null;
            string linkTutorialText = "";

            foreach (TutorialParagraph para in currentTutorial.CurrentPage.Paragraphs)
            {
                if (para.Type == ParagraphType.SwitchTutorial)
                {
                    linkedTutorial = para.m_Tutorial;
                    linkTutorialText = para.Text;
                }
                else if (para.Type == ParagraphType.Narrative)
                {
                    narrative = para;
                }
                else if (para.Type == ParagraphType.Instruction)
                {
                    instruction = para;
                }
                else if (para.Type == ParagraphType.Image)
                {
                    if (para.Image != null)
                    {
                        ShowElement("TutorialMediaContainer");
                        rootVisualElement.Q("TutorialMedia").style.backgroundImage = para.Image;
                    }
                    else
                    {
                        HideElement("TutorialMediaContainer");
                    }
                }
                else if (para.Type == ParagraphType.Video)
                {
                    if (para.Video != null)
                    {
                        ShowElement("TutorialMediaContainer");
                        rootVisualElement.Q("TutorialMedia").style.backgroundImage = VideoPlaybackManager.GetTextureForVideoClip(para.Video);
                    }
                    else
                    {
                        HideElement("TutorialMediaContainer");
                    }
                }
            }

            var linkButton = rootVisualElement.Q<Button>("LinkButton");
            if (linkedTutorial != null)
            {
                UIElementsUtils.SetupButton(linkButton, () => StartEndLinkTutorial(linkedTutorial), Tr(linkTutorialText));
                UIElementsUtils.Show(linkButton);
            }
            else
            {
                // TODO the button is hidden if tutorial is null, making it a bit difficult to notice that we are actually
                // authoring a tutorial switch button.
                UIElementsUtils.Hide(linkButton);
            }

            if (narrative != null)
            {
                rootVisualElement.Q<Label>("TutorialTitle").text = narrative.Title;
                RichTextToVisualElements(narrative.Text, rootVisualElement.Q("TutorialStepBox1"));
            }

            if (instruction == null || (string.IsNullOrEmpty(instruction.Text) && string.IsNullOrEmpty(instruction.Title)))
            {
                // hide instruction box if no text
                HideElement("InstructionContainer");
            }
            else
            {
                // populate instruction box
                ShowElement("InstructionContainer");
                if (string.IsNullOrEmpty(instruction.Title))
                    HideElement("InstructionTitle");
                else
                    ShowElement("InstructionTitle");
                rootVisualElement.Q<Label>("InstructionTitle").text = instruction.Title;
                RichTextToVisualElements(instruction.Text, rootVisualElement.Q("InstructionDescription"));
            }

            UpdatePageState();
        }

        void StartEndLinkTutorial(Tutorial endLink)
        {
            TutorialManager.Instance.IsTransitioningBetweenTutorials = true;
            TutorialManager.Instance.StartTutorial(endLink);
        }

        /// <summary>
        /// Sets the instruction highlight to green or blue and toggles between arrow and checkmark
        /// </summary>
        void UpdateInstructionBox()
        {
            if (CanMoveToNextPage && currentTutorial.CurrentPage.HasCriteria())
            {
                ShowElement("InstructionHighlightGreen");
                HideElement("InstructionHighlightBlue");
                ShowElement("InstructionCheckmark");
                HideElement("InstructionArrow");
            }
            else
            {
                HideElement("InstructionHighlightGreen");
                ShowElement("InstructionHighlightBlue");
                HideElement("InstructionCheckmark");
                ShowElement("InstructionArrow");
            }
        }

        // TODO move header stuff to UpdateHeader(), rename this function to UpdateButtonStates() or similar.
        void UpdatePageState()
        {
            Debug.Assert(currentTutorial);
            // It's possible to end up here while having an empty window (unit tests for example), abort in that case.
            if (rootVisualElement.childCount == 0)
                return;

            rootVisualElement.Q<Label>("HeaderLabel").text = currentTutorial.TutorialTitle;
            rootVisualElement.Q<Label>("StepCount").text = $"{currentTutorial.CurrentPageIndex + 1} / {currentTutorial.m_Pages.Count}";

            var nextButton = rootVisualElement.Q<Button>("NextButton");
            // By default, disable Next/Done button if we have criteria; it will be enabled
            // shortly if we're able to proceed to the next page, or to quit the tutorial.
            nextButton.SetEnabled(!currentTutorial.CurrentPage.HasCriteria());
            nextButton.text = m_NextButtonText;

            rootVisualElement.Q<Button>("PreviousButton").text = m_BackButtonText;

            // Enable/disable the hardcoded highlighting of the first page.
            if (IsFirstPage())
            {
                ShowElement("NextButtonBase");
            }
            else
            {
                HideElement("NextButtonBase");
            }
            // TODO delayCall needed for now as some criteria don't have up-to-date state when at the moment
            // we call this function, causing canMoveToNextPage to return false even though the criteria
            // are completed.
            EditorApplication.delayCall += () =>
            {
                UpdateInstructionBox();
                nextButton.SetEnabled(CanMoveToNextPage);
            };
        }

        void OnCriterionCompleted(Criterion criterion)
        {
            // The criterion might be non-pertinent for the window (e.g. when running unit tests)
            // TODO Ideally we'd subscribe only to the criteria of the current page so we don't need to check this
            if (!currentTutorial ||
                !currentTutorial.Pages
                    .SelectMany(page => page.Paragraphs)
                    .SelectMany(para => para.Criteria)
                    .Select(crit => crit.Criterion)
                    .Contains(criterion)
            )
            {
                return;
            }

            UpdatePageState();
        }

        void CreateTutorialMenuCards(VisualTreeAsset vistree, VisualElement cardContainer)
        {
            // If we have active container, use its sections, else we are viewing containers (or nothing).
            m_Cards = ActiveContainer != null
                ? ActiveContainer.Sections
                    .OrderBy(section => section.OrderInView)
                    .Select(section => section.IsTutorial
                        ? (Card)new TutorialCard { Target = section }  // cast required to work around CS0173
                        : (Card)new LinkCard { Target = section }
                    )
                    .ToArray()
                : m_Containers
                    .OrderBy(container => container.Title.Untranslated) // simply ordering containers alphabetically for now
                    .Select(container => new ContainerCard { Target = container })
                    .ToArray();

            if (m_Cards.OfType<TutorialCard>().Any(card => card.Section.Tutorial?.ProgressTrackingEnabled ?? false))
            {
                // Viewing tutorials which at least some have progress tracking enabled: make sure to fetch the statuses.
                // // For the time being, load the cached states so that for example a tutorial we just completed is shown
                // as completed as the up-to-date information might not have arrived just yet from the backend.
                ActiveContainer.Sections.ToList().ForEach(s => s.LoadState());
                FetchAllTutorialStates();
                EditorCoroutineUtility.StartCoroutineOwnerless(UpdateCheckmarksWhenStatesFetched());
            }

            foreach (var card in m_Cards)
            {
                switch (card)
                {
                    case TutorialCard tutorialCard:
                        card.Element = vistree.CloneTree().Q("TutorialsContainer").Q("CardContainer");
                        // NOTE Setting up the checkmark at this point might be futile as we just requested the states from the backend.
                        UpdateCheckmark(tutorialCard);
                        card.Element.RegisterCallback((MouseUpEvent evt) => tutorialCard.Section.StartTutorial());
                        break;

                    case LinkCard linkCard:
                        card.Element = vistree.CloneTree().Q("TutorialsContainer").Q("LinkCardContainer");
                        // Make sure link cards don't have completion markers
                        UpdateCheckmark(linkCard);
                        card.Element.RegisterCallback((MouseUpEvent evt) => linkCard.Section.OpenUrl());
                        break;

                    case ContainerCard containerCard:
                        card.Element = vistree.CloneTree().Q("TutorialsContainer").Q("CategoryCardContainer");
                        card.Element.RegisterCallback((MouseUpEvent evt) => ActiveContainer = containerCard.Container);
                        break;
                }

                card.Element.Q<Label>("TutorialName").text = card.Heading;
                card.Element.Q<Label>("TutorialDescription").text = card.Text;
                card.Element.tooltip = card.Tooltip;
                if (card.Image != null)
                    card.Element.Q("TutorialImage").style.backgroundImage = Background.FromTexture2D(card.Image);

                cardContainer.Add(card.Element);
            }
        }

        IEnumerator UpdateCheckmarksWhenStatesFetched()
        {
            while (!m_DoneFetchingTutorialStates)
            {
                yield return null;
            }

            foreach (var card in m_Cards.OfType<TutorialCard>())
            {
                UpdateCheckmark(card);
            }
        }

        void UpdateCheckmark(SectionCard card)
        {
            bool progresstracking = (card.Section.Tutorial != null && card.Section.Tutorial.ProgressTrackingEnabled);
            card.Element.Q<Label>("CompletionStatus").text = (card.Section.TutorialCompleted && progresstracking) ? Tr("COMPLETED") : "";
            UIElementsUtils.SetVisible(card.Element.Q("TutorialCheckmark"), progresstracking && card.Section.TutorialCompleted);
        }

        void RenderMediaIfPossible()
        {
            // Possible media is always at the first paragraph.
            var paragraph = currentTutorial?.CurrentPage?.Paragraphs.FirstOrDefault();

            if (paragraph == null)
            { return; }

            switch (paragraph.Type)
            {
                case ParagraphType.Image:
                    if (m_VideoTextureCache == null)
                    {
                        m_VideoTextureCache = paragraph.Image;
                        UpdateVideoFrame(m_VideoTextureCache);
                    }
                    break;
                case ParagraphType.Video:
                    if (paragraph.Video != null)
                    {
                        m_VideoTextureCache = VideoPlaybackManager.GetTextureForVideoClip(paragraph.Video);
                        UpdateVideoFrame(m_VideoTextureCache);
                        Repaint();
                    }
                    break;
            }
        }

        void OnEnable()
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(DeferredOnEnable());
            m_Containers.ForEach(SubscribeToContainer);
        }

        IEnumerator DeferredOnEnable()
        {
            m_IsInitialized = false;
            m_CurrentEditorLanguage = LocalizationDatabaseProxy.currentEditorLanguage;

            if (EditorApplication.isPlaying)
            {
                yield return null;
            }
            else
            {
                if (m_WaitForStylesRoutine != null)
                {
                    EditorCoroutineUtility.StopCoroutine(m_WaitForStylesRoutine);
                }
                m_WaitForStylesRoutine = EditorCoroutineUtility.StartCoroutineOwnerless(WaitUntilStylesAreAvailable());
                yield return m_WaitForStylesRoutine;
            }

            AssetDatabase.Refresh();
            AddCallbacksToEvents();
            InitializeUI();
            m_IsInitialized = true;
        }

        IEnumerator WaitUntilStylesAreAvailable()
        {
            StyleSheet darkStyle;
            do
            {
                //since loading from disk is an heavy operation that can freeze the editor, we don't want to do it every frame
                yield return new EditorWaitForSeconds(0.1f);
                //note: the type of style is meaningless. What we want to check is if they can be loaded or not.
                darkStyle = UIElementsUtils.LoadUIAsset<StyleSheet>("Main_Dark.uss");
            } while (!darkStyle);

            m_WaitForStylesRoutine = null;
        }

        void AddCallbacksToEvents()
        {
            Criterion.CriterionCompleted.AddListener(OnCriterionCompleted);
            // test for page completion state changes (rather than criteria completion/invalidation directly)
            // so that page completion state will be up-to-date
            TutorialPage.CriteriaCompletionStateTested.AddListener(OnTutorialPageCriteriaCompletionStateTested);
            TutorialPage.TutorialPageMaskingSettingsChanged.AddListener(OnTutorialPageMaskingSettingsChanged);
            TutorialPage.TutorialPageNonMaskingSettingsChanged.AddListener(OnTutorialPageNonMaskingSettingsChanged);
            EditorApplication.playModeStateChanged -= TrackPlayModeChanging;
            EditorApplication.playModeStateChanged += TrackPlayModeChanging;
        }

        void InitializeUI()
        {
            m_WindowTitleContent = Tr("Tutorials");
            // Set here in addition to CreateWindow() so that title of old saved layouts is overwritten,
            // also making sure that the title is translated always.
            titleContent.text = m_WindowTitleContent;
            m_PromptOk = Tr("OK");
            // Unity's menu guide convetion: text in bold, '>' used as a separator
            // NOTE EditorUtility.DisplayDialog doesn't support rich text so cannot use it here
            m_MenuPathGuide = Tr(MenuItems.Menu) + " > " + Tr(MenuItems.ShowTutorials);
            m_TabClosedDialogTitle = Tr("Close Tutorials");
            m_TabClosedDialogText = string.Format(Tr("You can find Tutorials later by choosing {0} in the top menu."), m_MenuPathGuide);
            m_SelectTutorialsText = Tr("Select Tutorials:");

            rootVisualElement.Clear();

            // Draw authoring toolbar always in authoring mode
            if (s_AuthoringMode)
                rootVisualElement.Add(new IMGUIContainer(DrawAuthoringToolbar));

            var windowState = currentTutorial != null
                ? State.TutorialInProgress
                : (ActiveContainer != null ? State.TutorialSelection : State.ContainerSelection);
            if (windowState == State.ContainerSelection && !m_Containers.Any())
            {
                // UI will be empty
                return;
            }

            IMGUIContainer videoBox = new IMGUIContainer(RenderMediaIfPossible);
            videoBox.style.alignSelf = new StyleEnum<Align>(Align.Center);
            videoBox.name = "VideoBox";

            var topBarAsset = UIElementsUtils.LoadUIAsset<VisualTreeAsset>("Main.uxml");
            TemplateContainer topBarTemplate = topBarAsset.CloneTree();
            // TODO consider caching these so that we don't need to do perform look-up for them many times
            // in various places in the code.
            VisualElement topBarVisElement = topBarTemplate.Q("TitleHeader");
            VisualElement footerBar = topBarTemplate.Q("TutorialActions");
            VisualElement tutorialImage = topBarTemplate.Q("TutorialImage");
            VisualElement tutorialMenuCard = topBarTemplate.Q("CardContainer");
            VisualElement linkButton = topBarTemplate.Q("LinkButton");
            VisualElement cardContainer = topBarTemplate.Q("TutorialListScrollView");
            cardContainer.style.alignItems = Align.Center;

            if (windowState != State.TutorialInProgress)
            {
                CreateTutorialMenuCards(topBarAsset, cardContainer);
            }

            var tutorialContentAsset = UIElementsUtils.LoadUIAsset<VisualTreeAsset>("TutorialContents.uxml");
            TemplateContainer tutorialContentTemplate = tutorialContentAsset.CloneTree();
            VisualElement tutorialContents = tutorialContentTemplate.Q("TutorialEmptyContents");
            tutorialContents.Q<Label>("SelectTutorialsLabel").text = m_SelectTutorialsText;
            tutorialContents.style.flexGrow = 1f;
            VisualElement tutorialContentPage = tutorialContentTemplate.Q("TutorialPageContainer");
            VisualElement tutorialTopBar = tutorialContentPage.Q("Header");
            tutorialContents.Add(cardContainer);

            TextElement titleElement = topBarVisElement.Q<TextElement>("TitleLabel");
            TextElement contextTextElement = topBarVisElement.Q<TextElement>("ContextLabel");

            UpdateHeader(contextTextElement, titleElement, topBarVisElement);

            var root = rootVisualElement;
            root.Add(tutorialTopBar);
            root.Add(videoBox);
            root.Add(topBarVisElement);
            root.Add(tutorialContents);

            Styles.ApplyThemeStyleSheetTo(root);

            VisualElement tutorialContainer = tutorialContentPage.Q("TutorialContainer");
            tutorialContainer.Add(linkButton);
            root.Add(tutorialContainer);

            footerBar.Q<Button>("PreviousButton").clicked += OnPreviousButtonClicked;
            footerBar.Q<Button>("NextButton").clicked += OnNextButtonClicked;

            VideoPlaybackManager.OnEnable();

            GUIViewProxy.PositionChanged += OnGUIViewPositionChanged;
            HostViewProxy.actualViewChanged += OnHostViewActualViewChanged;

            root.Add(footerBar);
            SetUpTutorial();

            MaskingEnabled = true;

            EditorCoroutineUtility.StartCoroutineOwnerless(InitializeVideoPlayer());

            SetWindowState(windowState);
        }

        void OnCloseClicked(MouseUpEvent mouseup)
        {
            ExitTutorial();
        }

        void GoBackToContainerSelection()
        {
            ActiveContainer = null;
        }

        void SetWindowState(State state)
        {
            if (ActiveContainer)
                UnsubscribeFromContainer(ActiveContainer);
            m_Containers.ForEach(UnsubscribeFromContainer);

            if (state == State.ContainerSelection)
            {
                // Viewing all containers: events of all containers are of interest
                m_Containers.ForEach(SubscribeToContainer);

                HideElement("TitleHeader");
                HideElement("TutorialActions");
                HideElement("Header");
                ShowElement("TutorialEmptyContents");
                HideElement("TutorialContainer");
                ShowElement("SelectTutorialsContainer");
            }
            else if (state == State.TutorialSelection)
            {
                Debug.Assert(ActiveContainer != null);
                // Viewing a single containers: only its events are of interest
                SubscribeToContainer(ActiveContainer);

                var backButton = rootVisualElement.Q("BackToContainers");
                bool hasMultipleContainers = m_Containers.Count() > 1;
                if (hasMultipleContainers)
                {
                    backButton.tooltip = Tr("Back to the tutorial project selection");
                    backButton.RegisterCallback<MouseUpEvent>(_ => GoBackToContainerSelection());
                }
                UIElementsUtils.SetVisible(backButton, hasMultipleContainers);
                ShowElement("TitleHeader");
                HideElement("TutorialActions");
                HideElement("Header");
                ShowElement("TutorialEmptyContents");
                HideElement("TutorialContainer");
                HideElement("SelectTutorialsContainer");
            }
            else // Tutorial in progress
            {
                // Viewing a a tutorial, its container's data is not currently visible in the header or elsewhere
                // so its events are not of interest.

                HideElement("TitleHeader");
                ShowElement("TutorialActions");
                VisualElement headerElement = rootVisualElement.Q("Header");
                UIElementsUtils.Show(headerElement);
                headerElement.Q("Close").RegisterCallback<MouseUpEvent>(OnCloseClicked);
                HideElement("TutorialEmptyContents");
                ShowElement("TutorialContainer");
                HideElement("SelectTutorialsContainer");
            }
        }

        void ShowElement(string name) => UIElementsUtils.Show(rootVisualElement.Q(name));
        void HideElement(string name) => UIElementsUtils.Hide(rootVisualElement.Q(name));

        // For the teardown callbacks the order of execution is OnBecameInvisible, OnDisable, OnDestroy.
        void OnBecameInvisible()
        {
            // Make sure the proper exit procedures are executed if user closes the window while
            // running tutorials; typically the user does not close the window, only exists tutorials
            // by clicking X or Done buttons.
            if (currentTutorial && !TutorialManager.Instance.IsTransitioningBetweenTutorials)
                ExitTutorial();
        }

        void OnDisable()
        {
            if (m_WaitForStylesRoutine != null)
            {
                EditorCoroutineUtility.StopCoroutine(m_WaitForStylesRoutine);
                m_WaitForStylesRoutine = null;
            }

            if (!m_PlayModeChanging)
            {
                AnalyticsHelper.TutorialEnded(TutorialConclusion.Quit);
            }

            Criterion.CriterionCompleted.RemoveListener(OnCriterionCompleted);

            if (currentTutorial)
            {
                ClearTutorialListener(currentTutorial);
                currentTutorial.StopTutorial();
            }
            TutorialPage.CriteriaCompletionStateTested.RemoveListener(OnTutorialPageCriteriaCompletionStateTested);
            TutorialPage.TutorialPageMaskingSettingsChanged.RemoveListener(OnTutorialPageMaskingSettingsChanged);
            TutorialPage.TutorialPageNonMaskingSettingsChanged.RemoveListener(OnTutorialPageNonMaskingSettingsChanged);
            GUIViewProxy.PositionChanged -= OnGUIViewPositionChanged;
            HostViewProxy.actualViewChanged -= OnHostViewActualViewChanged;
            m_Containers.ForEach(UnsubscribeFromContainer);

            VideoPlaybackManager.OnDisable();

            ApplyMaskingSettings(false);
        }

        void OnDestroy()
        {
            m_VideoTextureCache = null;

            // TODO this is in both OnDisable and OnDestroy. Shouldn't only OnDestroy suffice?
            if (m_WaitForStylesRoutine != null)
            {
                EditorCoroutineUtility.StopCoroutine(m_WaitForStylesRoutine);
                m_WaitForStylesRoutine = null;
            }

            // Play mode might trigger layout change (maximize on play) and closing of this window also.
            if (ShowTutorialsClosedDialog && !TutorialManager.IsLoadingLayout && !m_PlayModeChanging)
            {
                // Delay call prevents us getting the dialog upon assembly reload.
                EditorApplication.delayCall += delegate
                {
                    ShowTutorialsClosedDialog.SetValue(false);
                    EditorUtility.DisplayDialog(m_TabClosedDialogTitle, m_TabClosedDialogText, m_PromptOk);
                };
            }
        }

        // TODO Review the need for this. Remember to take  welcome dialog's masking into account.
        void OnHostViewActualViewChanged()
        {
            if (TutorialManager.IsLoadingLayout) { return; }
            // do not mask immediately in case unmasked GUIView doesn't exist yet
            // TODO disabled for now in order to get Welcome dialog masking working
            //QueueMaskUpdate();
        }

        void QueueMaskUpdate()
        {
            EditorApplication.update -= ApplyQueuedMask;
            EditorApplication.update += ApplyQueuedMask;
        }

        void OnTutorialPageCriteriaCompletionStateTested(TutorialPage sender)
        {
            if (currentTutorial == null || currentTutorial.CurrentPage != sender) { return; }

            if (sender.AreAllCriteriaSatisfied && sender.AutoAdvanceOnComplete && !sender.HasMovedToNextPage)
            {
                EditorCoroutineUtility.StartCoroutineOwnerless(GoToNextPageAfterDelay());
                return;
            }

            UpdatePageState();
            ApplyMaskingSettings(true);

            // Handle the special case of last page having criteria
            if (IsLastPage() && currentTutorial.CurrentPage.HasCriteria())
                currentTutorial.TryGoToNextPage();
        }

        // TODO refactor so that this function simply goes the next page as the function name implies
        // and move other logic to more appropriate places
        IEnumerator GoToNextPageAfterDelay()
        {
            yield return new EditorWaitForSeconds(0.5f);

            if (currentTutorial != null)
            {
                if (currentTutorial.TryGoToNextPage())
                    UpdatePageState();
                else if (IsLastPage())
                    ExitTutorial(); // auto-advanced the last page
                yield break;
            }
            ApplyMaskingSettings(true);
        }

        IEnumerator ExitTutorialAndPlaymode()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                while (EditorApplication.isPlaying)
                {
                    yield return null;
                }
            }
            else
            {
                yield return null;
            }
            ExitTutorial();
        }

        void ExitTutorial()
        {
            Debug.Assert(currentTutorial, "Quitting tutorial but tutorial is null");

            if (EditorApplication.isPlaying)
            {
                /* Note: this requires a frame anyway, so the save dialog won't show
                 * if we want to support the save dialog even in that case, then we should use "ExitTutorialAndPlaymode" coroutine 
                 * instead of directly calling this method.
                 * However, using that coroutine breaks the tutorial switching system due to race conditions.
                 * I'm leaving both that routine and this comment here so we know what to do in the future.
                 */
                EditorApplication.isPlaying = false;
            }

            if (!TutorialManager.Instance.IsTransitioningBetweenTutorials
                && !EditorApplication.isPlaying && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            currentTutorial.RaiseQuit();
            if (!currentTutorial.IsCompleted)
                currentTutorial.CurrentPage.RaiseOnBeforeQuitTutorial();

            SetTutorial(null);
            if (!TutorialManager.Instance.IsTransitioningBetweenTutorials)
                TutorialManager.Instance.RestoreOriginalState();
            ResetTutorial();
            // If auto-docking mechanism is used, the recreation of TutorialWindow is not triggered as is done
            // with layout loading mechanism.
            InitializeUI();
        }

        void OnTutorialInitiated(Tutorial sender)
        {
            if (!currentTutorial) { return; }

            AnalyticsHelper.TutorialStarted(currentTutorial);
            if (currentTutorial.ProgressTrackingEnabled)
                GenesisHelper.LogTutorialStarted(currentTutorial.LessonId);
            CreateTutorialViews();
        }

        void OnTutorialCompleted(Tutorial sender)
        {
            Debug.Assert(sender == currentTutorial);
            // After the tutorial is completed once, there's no longer need to report its possible repeated completions,
            // for example going back and forth between the second-to-last and last page.
            currentTutorial.Completed.RemoveListener(OnTutorialCompleted);

            AnalyticsHelper.TutorialEnded(TutorialConclusion.Completed);
            if (currentTutorial.ProgressTrackingEnabled)
            {
                GenesisHelper.LogTutorialEnded(currentTutorial.LessonId);
                MarkTutorialCompleted(currentTutorial.LessonId, currentTutorial.IsCompleted);
            }
        }

        internal void CreateTutorialViews()
        {
            m_AllParagraphs = currentTutorial.Pages.SelectMany(pg => pg.Paragraphs).ToList();
        }

        List<TutorialParagraph> GetCurrentParagraph()
        {
            if (m_Indexes == null || m_Indexes.Length != currentTutorial.PageCount)
            {
                // Update page to paragraph index
                m_Indexes = new int[currentTutorial.PageCount];
                var pageIndex = 0;
                var paragraphIndex = 0;
                foreach (var page in currentTutorial.Pages)
                {
                    m_Indexes[pageIndex++] = paragraphIndex;
                    if (page != null)
                        paragraphIndex += page.Paragraphs.Count();
                }
            }

            List<TutorialParagraph> tmp = new List<TutorialParagraph>();
            if (m_Indexes.Length > 0)
            {
                var endIndex = currentTutorial.CurrentPageIndex + 1 > currentTutorial.PageCount - 1
                    ? m_AllParagraphs.Count
                    : m_Indexes[currentTutorial.CurrentPageIndex + 1];
                for (int i = m_Indexes[currentTutorial.CurrentPageIndex]; i < endIndex; i++)
                {
                    tmp.Add(m_AllParagraphs[i]);
                }
            }
            return tmp;
        }

        internal void PrepareNewPage()
        {
            if (currentTutorial == null) return;
            if (!m_AllParagraphs.Any())
            {
                CreateTutorialViews();
            }
            m_Paragraphs.Clear();

            if (currentTutorial.CurrentPage == null)
            {
                m_NextButtonText = string.Empty;
            }
            else
            {
                m_NextButtonText = IsLastPage()
                    ? currentTutorial.CurrentPage.DoneButton
                    : currentTutorial.CurrentPage.NextButton;
            }
            m_BackButtonText = IsFirstPage() ? Tr("All Tutorials") : Tr("Back");

            m_Paragraphs = GetCurrentParagraph();

            m_Paragraphs.TrimExcess();

            EditorCoroutineUtility.StartCoroutineOwnerless(DelayedShowCurrentTutorialContent());
        }

        IEnumerator DelayedShowCurrentTutorialContent()
        {
            while (!m_IsInitialized)
            {
                yield return null;
            }
            ShowCurrentTutorialContent();
        }

        internal void ForceInititalizeTutorialAndPage()
        {
            m_FarthestPageCompleted = -1;

            CreateTutorialViews();
            PrepareNewPage();
        }

        bool IsLastPage() { return currentTutorial != null && currentTutorial.PageCount - 1 <= currentTutorial.CurrentPageIndex; }

        bool IsFirstPage() { return currentTutorial != null && currentTutorial.CurrentPageIndex == 0; }

        // Returns true if some real progress has been done (criteria on some page finished).
        bool IsInProgress()
        {
            return currentTutorial
                ?.Pages.Any(pg => pg.Paragraphs.Any(p => p.Criteria.Any() && pg.AreAllCriteriaSatisfied))
                ?? false;
        }

        void ClearTutorialListener(Tutorial tutorial)
        {
            tutorial.Initiated.RemoveListener(OnTutorialInitiated);
            tutorial.Completed.RemoveListener(OnTutorialCompleted);
            tutorial.PageInitiated.RemoveListener(OnShowPage);
        }

        internal void SetTutorial(Tutorial tutorial)
        {
            if (currentTutorial)
            {
                currentTutorial.StopTutorial();
                ClearTutorialListener(currentTutorial);
            }

            TutorialManager.Instance.IsTransitioningBetweenTutorials = false;
            currentTutorial = tutorial;
            // "set up" before resetting: resetting raises event we are interested in
            SetUpTutorial();
            if (currentTutorial)
                currentTutorial.ResetProgress();

            ApplyMaskingSettings(currentTutorial != null);
        }

        void SetUpTutorial()
        {
            // bail out if this instance no longer exists such as when e.g., loading a new window layout
            if (this == null || currentTutorial == null || currentTutorial.CurrentPage == null) { return; }

            if (currentTutorial.CurrentPage != null)
            {
                currentTutorial.CurrentPage.Initiate();
            }

            ClearTutorialListener(currentTutorial);

            currentTutorial.Initiated.AddListener(OnTutorialInitiated);
            currentTutorial.Completed.AddListener(OnTutorialCompleted);
            currentTutorial.PageInitiated.AddListener(OnShowPage);

            if (m_AllParagraphs.Any())
            {
                PrepareNewPage();
                return;
            }
            ForceInititalizeTutorialAndPage();
        }

        void ApplyQueuedMask()
        {
            if (IsParentNull()) { return; }

            EditorApplication.update -= ApplyQueuedMask;
            ApplyMaskingSettings(true);
        }

        IEnumerator InitializeVideoPlayer()
        {
            yield return null;

            do
            {
                yield return null;
                m_VideoBoxElement = rootVisualElement.Q("TutorialMediaContainer");
            }
            while (m_VideoBoxElement == null);


            if (currentTutorial == null)
            {
                if (m_VideoBoxElement != null)
                {
                    UIElementsUtils.Hide(m_VideoBoxElement);
                }
            }
            VideoPlaybackManager.OnEnable();
        }

        void OnPreviousButtonClicked()
        {
            if (IsFirstPage())
            {
                ExitTutorial();
            }
            else
            {
                currentTutorial.GoToPreviousPage();
            }
        }

        void OnNextButtonClicked()
        {
            if (CanMoveToNextPage && !currentTutorial.TryGoToNextPage()) // false means we have clicked "Done".
                ExitTutorial();
        }

        /// <summary>
        /// Clears the contents of this window. Use this before saving layouts for tutorials.
        /// </summary>
        internal void ClearContent()
        {
            m_AllParagraphs.Clear();
            SetTutorial(null);
            ClearContainers();
            ActiveContainer = null;
        }

        void DrawAuthoringToolbar()
        {
            const float buttonWidth = 30f;

            GUIContent IconContent(string iconName, string tooltip) =>
                EditorGUIUtility.IconContent(iconName, "|" + tooltip); // "|" needed for text to appear as tooltip

            bool Button(string iconName, string tooltip) =>
                GUILayout.Button(IconContent(iconName, tooltip), EditorStyles.toolbarButton, GUILayout.Width(buttonWidth));

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.ExpandWidth(true));

            using (new EditorGUI.DisabledScope(ActiveContainer == null))
            {
                if (Button("VerticalLayoutGroup Icon", Tr("Select Container")))
                {
                    Selection.activeObject = ActiveContainer;
                }
            }

            using (new EditorGUI.DisabledScope(currentTutorial == null))
            {
                if (Button("HorizontalLayoutGroup Icon", Tr("Select Tutorial")))
                {
                    Selection.activeObject = currentTutorial;
                }

                using (new EditorGUI.DisabledScope(currentTutorial?.CurrentPage == null))
                {
                    if (Button("UnityEditor.ConsoleWindow", Tr("Select Page")))
                    {
                        Selection.activeObject = currentTutorial.CurrentPage;
                    }
                }

                if (Button("endButton", Tr("Skip To End")))
                {
                    currentTutorial.SkipToLastPage();
                    currentTutorial.TryGoToNextPage(); // needed to trigger completion event
                }
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(currentTutorial == null))
            {
                EditorGUI.BeginChangeCheck();
                MaskingEnabled = GUILayout.Toggle(
                    MaskingEnabled, IconContent("Mask Icon", Tr("Preview Masking")),
                    EditorStyles.toolbarButton, GUILayout.Width(buttonWidth)
                );
                if (EditorGUI.EndChangeCheck())
                {
                    ApplyMaskingSettings(true);
                    GUIUtility.ExitGUI();
                    return;
                }
            }

            if (Button("Refresh", Tr("Run Startup Code")))
            {
                UserStartupCode.RunStartupCode(TutorialProjectSettings.Instance);
            }

            EditorGUILayout.EndHorizontal();
        }

        void OnTutorialModified(Tutorial sender)
        {
            if (currentTutorial == null || currentTutorial != sender) { return; }

            CreateTutorialViews();
            ShowCurrentTutorialContent();

            ApplyMaskingSettings(true);
        }

        void OnTutorialPageMaskingSettingsChanged(TutorialPage sender)
        {
            if (currentTutorial == null || currentTutorial.CurrentPage != sender) { return; }

            ApplyMaskingSettings(true);
        }

        void OnTutorialPageNonMaskingSettingsChanged(TutorialPage sender)
        {
            if (currentTutorial == null || currentTutorial.CurrentPage != sender) { return; }

            ShowCurrentTutorialContent();
        }

        void OnShowPage(Tutorial sender, TutorialPage page, int index)
        {
            page.RaiseOnBeforePageShown();
            m_FarthestPageCompleted = Mathf.Max(m_FarthestPageCompleted, index - 1);
            ApplyMaskingSettings(true);

            AnalyticsHelper.PageShown(page, index);
            PrepareNewPage();

            VideoPlaybackManager.ClearCache();
            page.RaiseOnAfterPageShown();
        }

        void OnGUIViewPositionChanged(UnityObject sender)
        {
            if (TutorialManager.IsLoadingLayout || sender.GetType().Name == "TooltipView") { return; }

            ApplyMaskingSettings(true);
        }

        void ApplyMaskingSettings(bool applyMask)
        {
            if (!applyMask || !MaskingEnabled || currentTutorial == null
                || currentTutorial.CurrentPage == null || TutorialManager.IsLoadingLayout)
            {
                MaskingManager.Unmask();
                InternalEditorUtility.RepaintAllViews();
                return;
            }

            MaskingSettings maskingSettings = currentTutorial.CurrentPage.CurrentMaskingSettings;
            try
            {
                if (maskingSettings == null || !maskingSettings.Enabled)
                {
                    MaskingManager.Unmask();
                }
                else
                {
                    bool foundAncestorProperty;
                    var unmaskedViews = UnmaskedView.GetViewsAndRects(maskingSettings.UnmaskedViews, out foundAncestorProperty);
                    if (foundAncestorProperty)
                    {
                        // Keep updating mask when target property is not unfolded
                        QueueMaskUpdate();
                    }

                    if (currentTutorial.CurrentPageIndex <= m_FarthestPageCompleted)
                    {
                        unmaskedViews = new UnmaskedView.MaskData();
                    }

                    UnmaskedView.MaskData highlightedViews;

                    if (unmaskedViews.Count > 0) //Unmasked views should be highlighted
                    {
                        highlightedViews = (UnmaskedView.MaskData)unmaskedViews.Clone();
                    }
                    else if (CanMoveToNextPage) // otherwise, if the current page is completed, highlight this window
                    {
                        highlightedViews = new UnmaskedView.MaskData();
                        highlightedViews.AddParentFullyUnmasked(this);
                    }
                    else // otherwise, highlight manually specified control rects if there are any
                    {
                        var unmaskedControls = new List<GuiControlSelector>();
                        var unmaskedViewsWithControlsSpecified =
                            maskingSettings.UnmaskedViews.Where(v => v.GetUnmaskedControls(unmaskedControls) > 0).ToArray();
                        // if there are no manually specified control rects, highlight all unmasked views
                        highlightedViews = UnmaskedView.GetViewsAndRects(
                            unmaskedViewsWithControlsSpecified.Length == 0 ?
                            maskingSettings.UnmaskedViews : unmaskedViewsWithControlsSpecified
                        );
                    }

                    // ensure tutorial window's HostView and tooltips are not masked
                    unmaskedViews.AddParentFullyUnmasked(this);
                    unmaskedViews.AddTooltipViews();

                    // tooltip views should not be highlighted
                    highlightedViews.RemoveTooltipViews();

                    MaskingManager.Mask(
                        unmaskedViews,
                        Styles == null ? Color.magenta * new Color(1f, 1f, 1f, 0.8f) : Styles.MaskingColor,
                        highlightedViews,
                        Styles == null ? Color.cyan * new Color(1f, 1f, 1f, 0.8f) : Styles.HighlightColor,
                        Styles == null ? new Color(1, 1, 1, 0.5f) : Styles.BlockedInteractionColor,
                        Styles == null ? 3f : Styles.HighlightThickness
                    );
                }
            }
            catch (ArgumentException e)
            {
                if (s_AuthoringMode)
                    Debug.LogException(e, currentTutorial.CurrentPage);
                else
                    Console.WriteLine(StackTraceUtility.ExtractStringFromException(e));

                MaskingManager.Unmask();
            }
            finally
            {
                InternalEditorUtility.RepaintAllViews();
            }
        }

        void ResetTutorialOnDelegate(PlayModeStateChange playmodeChange)
        {
            switch (playmodeChange)
            {
                case PlayModeStateChange.EnteredEditMode:
                    EditorApplication.playModeStateChanged -= ResetTutorialOnDelegate;
                    ResetTutorial();
                    break;
            }
        }

        internal void ResetTutorial()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.playModeStateChanged += ResetTutorialOnDelegate;
                EditorApplication.isPlaying = false;
            }
            else
            {
                m_FarthestPageCompleted = -1;
                TutorialManager.Instance.ResetTutorial();
            }
        }

        private void SetEditorDeltaTime()
        {
            if (m_LastTimeSinceStartup == 0f)
            {
                m_LastTimeSinceStartup = EditorApplication.timeSinceStartup;
            }
            m_EditorDeltaTime = EditorApplication.timeSinceStartup - m_LastTimeSinceStartup;
            m_LastTimeSinceStartup = EditorApplication.timeSinceStartup;
        }

        void Update()
        {
            SetEditorDeltaTime();

            m_BlinkTick += m_EditorDeltaTime;
            m_CheckLanguageTick += m_EditorDeltaTime;

            currentTutorial?.CurrentPage?.RaiseOnTutorialPageStay();

            if (m_BlinkTick >= 1f)
            {
                m_BlinkTick = 0f;
                if (IsFirstPage())
                {
                    if (m_BlinkOn)
                    {
                        ShowElement("NextButtonBase");
                    }
                    else
                    {
                        HideElement("NextButtonBase");
                    }
                    m_BlinkOn = !m_BlinkOn;
                }
            }

            if (m_CheckLanguageTick >= 1f)
            {
                m_CheckLanguageTick = 0f;
                if (LocalizationDatabaseProxy.currentEditorLanguage != m_CurrentEditorLanguage)
                {
                    m_CurrentEditorLanguage = LocalizationDatabaseProxy.currentEditorLanguage;
                    InitializeUI();
                }
            }
        }

        /// <summary>
        /// Marks all tutorials (TutorialContainer.Sections) in the project and the potential cards created for them uncompleted.
        /// </summary>
        internal void MarkAllTutorialsUncompleted()
        {
            TutorialEditorUtils.FindAssets<TutorialContainer>()
                .SelectMany(container => container.Sections)
                .ToList()
                .ForEach(s => MarkTutorialCompleted(s.TutorialId, false));

            foreach (var card in m_Cards.OfType<TutorialCard>())
            {
                UpdateCheckmark(card);
            }
        }

        /// <summary>
        /// Fetches statuses for all known tutorials from the web API
        /// </summary>
        internal void FetchAllTutorialStates()
        {
            m_DoneFetchingTutorialStates = false;
            GenesisHelper.GetAllTutorials((tutorials) =>
            {
                tutorials.ForEach(t => MarkTutorialCompleted(t.lessonId, t.status == "Finished"));
                m_DoneFetchingTutorialStates = true;
            });
        }

        void MarkTutorialCompleted(string lessonId, bool completed)
        {
            // If the (un)completed tutorial is not found, meaning its container is not currently set to the window,
            // it doesn't matter: we will mark tutorials (un)completed once again when when (re)create the cards

            // NOTE Could consider caching Sections by Lesson Id but as we have only
            // have very few of them doesn't really matter too much for now.
            m_Containers
                .SelectMany(container => container.Sections)
                .Where(section => section.TutorialId == lessonId)
                .ToList()
                .ForEach(section =>
                {
                    section.TutorialCompleted = completed;
                    section.SaveState();
                });
        }
    }
}
