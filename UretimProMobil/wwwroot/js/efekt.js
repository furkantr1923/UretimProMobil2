// Ses efektleri (WebAudio) ve konfeti: dosya gerektirmez, çevrimdışı çalışır
window.efekt = (function () {
    let c = null;
    function ctx() { const A = window.AudioContext || window.webkitAudioContext; if (!A) return null; if (!c) c = new A(); if (c.state === 'suspended') c.resume(); return c; }
    function ton(f, bas, sure, tip, guc) {
        const a = ctx(); if (!a) return;
        const o = a.createOscillator(), g = a.createGain(), t = a.currentTime + bas;
        o.type = tip || 'sine'; o.frequency.value = f;
        g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(guc || 0.2, t + 0.015); g.gain.exponentialRampToValueAtTime(0.0001, t + sure);
        o.connect(g); g.connect(a.destination); o.start(t); o.stop(t + sure + 0.05);
    }
    const sesler = {
        bip: () => { ton(1568, 0, 0.09, 'square', 0.08); ton(2093, 0.09, 0.12, 'square', 0.07); },
        basari: () => { ton(523, 0, 0.14); ton(659, 0.11, 0.14); ton(784, 0.22, 0.14); ton(1047, 0.33, 0.35); },
        hata: () => { ton(220, 0, 0.18, 'sawtooth', 0.12); ton(180, 0.2, 0.28, 'sawtooth', 0.12); },
        alarm: () => { for (let i = 0; i < 4; i++) { ton(1046, i * 0.34, 0.16, 'sawtooth', 0.1); ton(784, i * 0.34 + 0.17, 0.16, 'sawtooth', 0.1); } }
    };
    function konfeti() {
        const k = document.createElement('canvas'); k.className = 'konfeti';
        k.width = innerWidth; k.height = innerHeight; document.body.appendChild(k);
        const x = k.getContext('2d'), r = ['#B8733A', '#D58E52', '#1E7A4C', '#4CC487', '#F6E7D8', '#E9B04B'];
        const p = Array.from({ length: 120 }, () => ({ x: innerWidth / 2, y: innerHeight / 3, vx: (Math.random() - .5) * 14, vy: Math.random() * -14 - 4, r: r[Math.floor(Math.random() * r.length)], s: 4 + Math.random() * 6, d: Math.random() * 6 }));
        let n = 0;
        (function ciz() {
            x.clearRect(0, 0, k.width, k.height);
            p.forEach(q => { q.vy += 0.35; q.x += q.vx; q.y += q.vy; q.d += 0.2; x.save(); x.translate(q.x, q.y); x.rotate(q.d); x.fillStyle = q.r; x.fillRect(-q.s / 2, -q.s / 4, q.s, q.s / 2); x.restore(); });
            if (++n < 140) requestAnimationFrame(ciz); else k.remove();
        })();
    }
    return {
        cal: ad => { try { sesler[ad] && sesler[ad](); } catch (e) { } },
        konfeti: konfeti,
        tema: k => document.documentElement.dataset.tema = k ? 'karanlik' : 'acik',
        odak: id => { const e = document.getElementById(id); if (e) e.focus(); }
    };
})();
