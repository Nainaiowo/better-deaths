(() => {
  const searchInput = document.querySelector('#help-search');
  const results = document.querySelector('#search-results');
  const menuButton = document.querySelector('.menu-button');
  const sidebar = document.querySelector('#help-sidebar');
  const navLinks = [...document.querySelectorAll('.sidebar nav a')];
  const sections = [...document.querySelectorAll('.searchable')];

  const helpTopics = [
    { id: 'start', href: 'index.html#start', title: 'Start here', keywords: 'onboarding begin setup first pull install', summary: 'What Better Deaths records and where to begin.' },
    { id: 'first-review', href: 'index.html#first-review', title: 'Your first death review', keywords: 'first pull select death summary leadup workflow', summary: 'Follow a recorded death from the pull list to its evidence.' },
    { id: 'review', href: 'review.html#review', title: 'Review deaths and the lead-up timeline', keywords: 'review pulls search filter clear collapse resize summary fatal event multi hit hp shields overkill enemy hp environmental non hit lead-up heal timers focused detailed newest oldest chat', summary: 'Understand the death timeline, Selected Death Summary, and captured lead-up.' },
    { id: 'what-if', href: 'review.html#what-if', title: 'What-if mitigation', keywords: 'what if mitigation feint addle reprisal targeted survival estimate', summary: 'Test captured fatal damage against alternate mitigation choices.' },
    { id: 'replay', href: 'replay.html#replay', title: 'Death Replay', keywords: 'replay playback scrub death markers skull speed trails arena positions movement mechanics party hp active effects debuffs mitigation zoom pan resize names classes waymark opacity focus beta', summary: 'Rebuild a pull with synchronized positions, mechanics, HP, and effects.' },
    { id: 'analyzer', href: 'analyzer.html#analyzer', title: 'WTF.DIG Analyzer', keywords: 'analyzer wtfdig mczub dmu dancing mad fflogs local pull arrows merry go round filipino freaky forsaken black hole kefka says limit cut', summary: 'Inspect supported Dancing Mad mechanics using local pulls or FFLogs.' },
    { id: 'current-pull', href: 'widget.html#current-pull', title: 'Current Pull Widget', keywords: 'current pull widget concise normal popup recap button death live last pull review move resize opacity icons preview scroll', summary: 'Keep a compact live death summary visible over gameplay.' },
    { id: 'options', href: 'settings.html#options', title: 'Options', keywords: 'options general popup privacy chat capture party others clock widget recorded pulls scrollbars redaction', summary: 'Control plugin behavior, capture, privacy, chat, and local death access.' },
    { id: 'customize', href: 'settings.html#customize', title: 'Customize', keywords: 'customize theme appearance opacity icon size colors fun mode ligma focused detailed newest oldest duration', summary: 'Adjust Review layout, presentation, colors, themes, and Fun Mode.' },
    { id: 'data', href: 'settings.html#data', title: 'Data and privacy', keywords: 'data privacy local files upload telemetry names redaction sharing chat fflogs service storage', summary: 'Understand what is stored locally and when information leaves the plugin.' },
    { id: 'troubleshooting', href: 'troubleshooting.html#troubleshooting', title: 'Troubleshooting', keywords: 'troubleshooting missing pull no replay old data incomplete markers debuffs fflogs widget redaction two hp bars arrow snapshot panels overlay estimated timing', summary: 'Resolve common capture, Replay, Analyzer, widget, and display questions.' },
    { id: 'commands', href: 'troubleshooting.html#commands', title: 'Commands', keywords: 'commands slash bd betterdeaths bdwidget betterdeathswidget', summary: 'Open Better Deaths or toggle its widget from chat.' },
    { id: 'glossary', href: 'troubleshooting.html#glossary', title: 'Glossary', keywords: 'glossary overkill shield snapshot mitigation debuff lead-up fatal event non-hit ko recorded mechanic data', summary: 'Plain-language definitions for terms used throughout Better Deaths.' },
  ];

  const currentSectionById = new Map(sections.map((section) => [section.id, section]));
  const searchIndex = helpTopics.map((topic) => {
    const section = currentSectionById.get(topic.id);
    return {
      ...topic,
      text: section?.textContent.replace(/\s+/g, ' ').trim() || `${topic.keywords} ${topic.summary}`,
    };
  });

  const requestedTopic = helpTopics.find((topic) => topic.id === location.hash.slice(1));
  if (requestedTopic && !currentSectionById.has(requestedTopic.id)) {
    location.replace(requestedTopic.href);
    return;
  }

  document.querySelectorAll('.guide-figure img').forEach((image) => {
    if (image.closest('.image-link')) return;

    const link = document.createElement('a');
    link.className = 'image-link';
    link.href = image.getAttribute('src');
    link.target = '_blank';
    link.rel = 'noopener';
    link.setAttribute('aria-label', `Open full-size image: ${image.alt || 'Better Deaths screenshot'}`);

    const action = document.createElement('span');
    action.className = 'image-action';
    action.textContent = 'View full size';

    image.replaceWith(link);
    link.append(image, action);
  });

  const closeSearch = () => {
    results.hidden = true;
    searchInput.setAttribute('aria-expanded', 'false');
  };

  const renderSearch = () => {
    const query = searchInput.value.trim().toLowerCase();
    if (!query) {
      closeSearch();
      return;
    }

    const terms = query.split(/\s+/).filter(Boolean);
    const matches = searchIndex
      .map((entry) => {
        const title = entry.title.toLowerCase();
        const haystack = `${entry.title} ${entry.keywords} ${entry.text}`.toLowerCase();
        const matchesAll = terms.every((term) => haystack.includes(term));
        const score = matchesAll
          ? terms.reduce((total, term) => total + (title.includes(term) ? 3 : entry.keywords.includes(term) ? 2 : 1), 0)
          : 0;
        return { ...entry, score };
      })
      .filter((entry) => entry.score > 0)
      .sort((a, b) => b.score - a.score)
      .slice(0, 8);

    results.replaceChildren();
    if (matches.length === 0) {
      const empty = document.createElement('p');
      empty.className = 'search-empty';
      empty.textContent = 'No matching help topics.';
      results.append(empty);
    } else {
      matches.forEach((match) => {
        const link = document.createElement('a');
        link.href = match.href;
        const title = document.createElement('strong');
        title.textContent = match.title;
        const summary = document.createElement('span');
        summary.textContent = match.summary || 'Open help topic';
        link.append(title, summary);
        link.addEventListener('click', () => {
          searchInput.value = '';
          closeSearch();
        });
        results.append(link);
      });
    }

    results.hidden = false;
    searchInput.setAttribute('aria-expanded', 'true');
  };

  searchInput.addEventListener('input', renderSearch);
  searchInput.addEventListener('focus', renderSearch);
  document.addEventListener('click', (event) => {
    if (!event.target.closest('.header-search')) closeSearch();
  });

  document.addEventListener('keydown', (event) => {
    const isTyping = event.target.matches('input, textarea, select, [contenteditable="true"]');
    if (event.key === '/' && !isTyping) {
      event.preventDefault();
      searchInput.focus();
    }
    if (event.key === 'Escape') {
      closeSearch();
      searchInput.blur();
      sidebar.classList.remove('open');
      document.body.classList.remove('nav-open');
      menuButton.setAttribute('aria-expanded', 'false');
    }
  });

  menuButton.addEventListener('click', () => {
    const open = sidebar.classList.toggle('open');
    document.body.classList.toggle('nav-open', open);
    menuButton.setAttribute('aria-expanded', String(open));
    menuButton.setAttribute('aria-label', open ? 'Close help navigation' : 'Open help navigation');
  });

  navLinks.forEach((link) => {
    link.addEventListener('click', () => {
      sidebar.classList.remove('open');
      document.body.classList.remove('nav-open');
      menuButton.setAttribute('aria-expanded', 'false');
      menuButton.setAttribute('aria-label', 'Open help navigation');
    });
  });

  const currentPath = location.pathname.split('/').pop() || 'index.html';
  const isCurrentPageLink = (link) => {
    const url = new URL(link.href, location.href);
    return (url.pathname.split('/').pop() || 'index.html') === currentPath;
  };
  const visibleSections = sections.filter((section) => navLinks.some((link) =>
    isCurrentPageLink(link) && link.hash === `#${section.id}`));
  const setActiveLink = (id) => {
    navLinks.forEach((link) => {
      const active = isCurrentPageLink(link) && link.hash === `#${id}`;
      link.classList.toggle('active', active);
      if (active) link.setAttribute('aria-current', 'location');
      else link.removeAttribute('aria-current');
    });
  };

  const observer = new IntersectionObserver((entries) => {
    const visible = entries
      .filter((entry) => entry.isIntersecting)
      .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
    if (visible[0]) setActiveLink(visible[0].target.id);
  }, { rootMargin: '-20% 0px -65% 0px', threshold: 0 });

  visibleSections.forEach((section) => observer.observe(section));
  setActiveLink(location.hash.slice(1) || visibleSections[0]?.id || 'start');
})();
