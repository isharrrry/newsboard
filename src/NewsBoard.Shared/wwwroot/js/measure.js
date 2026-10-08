(function () {
  'use strict';

  const states = new WeakMap();

  function byId(id) {
    return document.getElementById(id);
  }

  // 允许调用方传元素本身，也允许传元素 id（Blazor 侧拿不到元素，只能传 id）
  function resolve(target) {
    if (!target) {
      return null;
    }
    return typeof target === 'string' ? byId(target) : target;
  }

  function stateOf(el) {
    let s = states.get(el);
    if (!s) {
      s = { from: 0, distance: 0, duration: 0, startTs: 0, raf: 0, done: true };
      states.set(el, s);
    }
    return s;
  }

  function cancelFrame(s) {
    if (s.raf && window.cancelAnimationFrame) {
      window.cancelAnimationFrame(s.raf);
    }
    s.raf = 0;
  }

  function step(el, s, now) {
    if (s.done) {
      return;
    }
    const progress = s.duration <= 0 ? 1 : (now - s.startTs) / s.duration;
    const k = progress > 1 ? 1 : progress;
    el.scrollTop = s.from + s.distance * k;
    if (k >= 1) {
      s.done = true;
      s.raf = 0;
      return;
    }
    s.raf = window.requestAnimationFrame(function (ts) { step(el, s, ts); });
  }

  function run(el, from, distance, duration) {
    const s = stateOf(el);
    cancelFrame(s);
    if (!(distance > 0)) {
      s.done = true;
      el.scrollTop = from;
      return;
    }
    if (!(duration > 0)) {
      s.done = true;
      el.scrollTop = from + distance;
      return;
    }
    s.from = from;
    s.distance = distance;
    s.duration = duration;
    s.startTs = window.performance && window.performance.now ? window.performance.now() : Date.now();
    s.done = false;
    s.raf = window.requestAnimationFrame(function (ts) { step(el, s, ts); });
  }

  function overflowOf(el) {
    const scrollHeight = el.scrollHeight;
    const clientHeight = el.clientHeight;
    return {
      scrollHeight: scrollHeight,
      clientHeight: clientHeight,
      height: scrollHeight - clientHeight,
      overflow: scrollHeight - clientHeight > Math.max(1, clientHeight * 0.05)
    };
  }

  window.newsBoard = {
    /** 溢出测量：{ scrollHeight, clientHeight, height, overflow }（overflow 留 5% 余量） */
    measure: function (id) {
      const el = resolve(id);
      if (!el) {
        return null;
      }
      el.scrollTop = 0;
      return overflowOf(el);
    },

    /** 从当前位置匀速滚到底：durationMs 内滚完剩余溢出高度 */
    startScroll: function (target, durationMs) {
      const el = resolve(target);
      if (!el) {
        return false;
      }
      const info = overflowOf(el);
      const remaining = info.scrollHeight - info.clientHeight - el.scrollTop;
      run(el, el.scrollTop, remaining, durationMs);
      return true;
    },

    /** 朗读结束收尾：ms 内加速滚完剩余部分 */
    finishScroll: function (target, ms) {
      const el = resolve(target);
      if (!el) {
        return false;
      }
      const remaining = el.scrollHeight - el.clientHeight - el.scrollTop;
      run(el, el.scrollTop, remaining, ms);
      return true;
    },

    /** 立即停止滚动动画 */
    stopScroll: function (target) {
      const el = resolve(target);
      if (!el) {
        return false;
      }
      const s = stateOf(el);
      s.done = true;
      cancelFrame(s);
      return true;
    }
  };
})();
