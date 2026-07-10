window.optometriaClinicalSignature = (() => {
    const pads = new Map();

    function getPoint(event, canvas) {
        const rect = canvas.getBoundingClientRect();
        const source = event.touches && event.touches.length > 0 ? event.touches[0] : event;
        return {
            x: source.clientX - rect.left,
            y: source.clientY - rect.top
        };
    }

    function resizeCanvas(canvas) {
        const ratio = Math.max(window.devicePixelRatio || 1, 1);
        const rect = canvas.getBoundingClientRect();
        const width = Math.max(Math.floor(rect.width), 1);
        const height = Math.max(Math.floor(rect.height), 1);
        canvas.width = width * ratio;
        canvas.height = height * ratio;
        const context = canvas.getContext("2d");
        context.setTransform(ratio, 0, 0, ratio, 0, 0);
        context.lineWidth = 2;
        context.lineCap = "round";
        context.lineJoin = "round";
        context.strokeStyle = "#2c241d";
        context.fillStyle = "#fffdf8";
        context.fillRect(0, 0, width, height);
        return context;
    }

    function drawImageOnCanvas(canvas, dataUrl) {
        const context = resizeCanvas(canvas);
        if (!dataUrl) {
            return;
        }

        const image = new Image();
        image.onload = () => {
            context.drawImage(image, 0, 0, canvas.clientWidth, canvas.clientHeight);
        };
        image.src = dataUrl;
    }

    function init(canvasId, initialDataUrl) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            return false;
        }

        if (pads.has(canvasId)) {
            if (initialDataUrl) {
                drawImageOnCanvas(canvas, initialDataUrl);
                pads.get(canvasId).hasInk = true;
            }
            return true;
        }

        const context = resizeCanvas(canvas);
        let drawing = false;
        const pad = { hasInk: Boolean(initialDataUrl) };

        const start = (event) => {
            drawing = true;
            const point = getPoint(event, canvas);
            context.beginPath();
            context.moveTo(point.x, point.y);
            event.preventDefault();
        };

        const move = (event) => {
            if (!drawing) {
                return;
            }

            const point = getPoint(event, canvas);
            context.lineTo(point.x, point.y);
            context.stroke();
            pad.hasInk = true;
            event.preventDefault();
        };

        const end = (event) => {
            if (!drawing) {
                return;
            }

            drawing = false;
            context.closePath();
            event.preventDefault();
        };

        canvas.addEventListener("mousedown", start);
        canvas.addEventListener("mousemove", move);
        canvas.addEventListener("mouseup", end);
        canvas.addEventListener("mouseleave", end);
        canvas.addEventListener("touchstart", start, { passive: false });
        canvas.addEventListener("touchmove", move, { passive: false });
        canvas.addEventListener("touchend", end, { passive: false });
        canvas.addEventListener("touchcancel", end, { passive: false });

        window.addEventListener("resize", () => {
            const dataUrl = canvas.toDataURL("image/png");
            drawImageOnCanvas(canvas, dataUrl);
        });

        pads.set(canvasId, pad);
        if (initialDataUrl) {
            drawImageOnCanvas(canvas, initialDataUrl);
        }

        return true;
    }

    function clear(canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            return;
        }

        resizeCanvas(canvas);
        const pad = pads.get(canvasId);
        if (pad) {
            pad.hasInk = false;
        }
    }

    function getDataUrl(canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) {
            return "";
        }

        const pad = pads.get(canvasId);
        if (!pad || !pad.hasInk) {
            return "";
        }

        return canvas.toDataURL("image/png");
    }

    return {
        init,
        clear,
        getDataUrl
    };
})();
