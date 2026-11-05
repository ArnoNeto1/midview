document.addEventListener("DOMContentLoaded", () => {
  const revealElements = document.querySelectorAll(".reveal");
  if (!("IntersectionObserver" in window)) {
    revealElements.forEach((el) => el.classList.add("is-visible"));
    return;
  }
  const revealObserver = new IntersectionObserver((entries, observer) => {
    entries.forEach((entry) => {
      if (entry.isIntersecting) {
        entry.target.classList.add("is-visible");
        observer.unobserve(entry.target);
      }
    });
  }, { root: null, rootMargin: "0px 0px -10% 0px", threshold: 0.1 });
  revealElements.forEach((el) => revealObserver.observe(el));

  /* Ao clicar em Início, Funções, Como começar ou Privacidade no menu (Home,
     Features, Getting started ou Privacy em en/), repete o efeito cascata na
     seção de destino. */
  document.querySelectorAll('a[href^="#"]').forEach((link) => {
    link.addEventListener("click", () => {
      const hash = link.getAttribute("href");
      if (!hash || hash === "#") return;
      const scope = hash === "#inicio" || hash === "#top"
        ? document.querySelector(".hero")
        : document.querySelector(hash);
      if (!scope) return;
      const targets = scope.querySelectorAll(".reveal");
      targets.forEach((el) => {
        revealObserver.unobserve(el);
        el.style.transition = "none";
        el.classList.remove("is-visible");
      });
      void scope.offsetWidth;
      targets.forEach((el) => {
        el.style.transition = "";
        revealObserver.observe(el);
      });
    });
  });
});

/* Quantas vezes o instalador foi baixado, somando todas as versões publicadas
   no GitHub. Sem resposta da API, a linha continua escondida. A frase segue
   a língua da página (lang do <html>). */
fetch("https://api.github.com/repos/ArnoNeto1/midview/releases?per_page=100")
  .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
  .then((releases) => {
    const total = releases.reduce((soma, r) => soma + r.assets.reduce((s, a) => s + a.download_count, 0), 0);
    if (!total) return;
    const item = document.getElementById("downloads");
    const english = document.documentElement.lang.startsWith("en");
    item.querySelector("span").textContent = english
      ? `Downloaded ${total.toLocaleString("en-US")} ${total === 1 ? "time" : "times"}`
      : `Já baixado ${total.toLocaleString("pt-BR")} ${total === 1 ? "vez" : "vezes"}`;
    item.hidden = false;
  })
  .catch(() => {});
