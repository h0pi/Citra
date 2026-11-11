// Applies the saved (or OS) theme before Angular starts, so dark mode doesn't flash white on load.
// Must match ThemeService (key 'citra-theme', class 'app-dark').
(function () {
  try {
    var saved = localStorage.getItem('citra-theme');
    var dark = saved ? saved === 'dark' : window.matchMedia('(prefers-color-scheme: dark)').matches;
    if (dark) document.documentElement.classList.add('app-dark');
  } catch (e) {}
})();
