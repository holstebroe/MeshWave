const express = require('express');
const path = require('path');
const fs = require('fs');
const marked = require('marked');
const rateLimit = require('express-rate-limit');
const escapeHtml = require('escape-html');

const app = express();
const PORT = 8000;

// Serve static files from the website directory
app.use(express.static(path.join(__dirname)));

// Rate limiting to prevent DoS via file system access
const docLimiter = rateLimit({
    windowMs: 15 * 60 * 1000, // 15 minutes
    max: 100, // limit each IP to 100 requests per windowMs
    message: 'Too many requests from this IP, please try again after 15 minutes'
});

/**
 * Documentation route handler
 * Handles requests for the landing page (/documentation), query-based access (/documentation?source=Architecture), 
 * and direct markdown file access (/Documentation/P2P-Handshake.md).
 */
app.get('/documentation', docLimiter, (req, res) => {
    let source = null;
    let isPathAccess = false;

    // 1. Check for Direct Markdown File Access: /Documentation/P2P-Handshake.md
    const requestedPath = req.params[0];
    if (requestedPath && requestedPath.endsWith('.md')) {
        source = requestedPath.replace(/\.md$/, '');
        isPathAccess = true;
    } 
    // 2. Check for Query Parameter Source: /documentation?source=Architecture
    else if (req.query.source) {
        source = req.query.source;
        isPathAccess = false;
    }

    // No source specified -> serve documentation landing page
    if (!source) {
        return res.sendFile(
            path.join(__dirname, 'documentation.html')
        );
    }

    // Validate source to prevent path injection and XSS
    // Only allow alphanumeric, hyphen, underscore, and forward slash
    if (!/^[a-zA-Z0-9\-_/]+$/.test(source)) {
        return res.status(400).send('Invalid source parameter');
    }

    // Determine the markdown file path based on the source name
    const documentationDir = path.resolve(__dirname, '..', 'Documentation');
    const mdFilePath = path.join(documentationDir, `${source}.md`);

    // Verify the resolved path is still within the documentation directory
    if (!mdFilePath.startsWith(documentationDir)) {
        return res.status(403).send('Access denied');
    }

    // Check file exists
    if (!fs.existsSync(mdFilePath)) {
        return res.status(404).send(`
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>404 Not Found</title>
    <script src="https://cdn.tailwindcss.com"></script>
</head>
<body class="bg-gray-50 text-gray-900 p-10">
    <h1 class="text-3xl font-bold mb-4">404 Not Found</h1>
    <p>Markdown source "${escapeHtml(source)}" was not found.</p>
</body>
</html>
        `);
    }

    // Read markdown file
    fs.readFile(mdFilePath, 'utf8', (err, data) => {
        if (err) {
            console.error('Error reading markdown file:', err);

            return res.status(500).send(`
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>500 Internal Server Error</title>
    <script src="https://cdn.tailwindcss.com"></script>
</head>
<body class="bg-gray-50 text-gray-900 p-10">
    <h1 class="text-3xl font-bold mb-4">500 Internal Server Error</h1>
    <p>Could not read the documentation source.</p>
</body>
</html>
            `);
        }

        const htmlContent = marked.parse(data);

        // Determine title based on source for better SEO/UX
        const pageTitle = `${escapeHtml(source)} | MeshWave Documentation`;


        const finalHtml = `
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>${pageTitle}</title>

    <script src="https://cdn.tailwindcss.com"></script>

    <link
        rel="stylesheet"
        href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.0.0/css/all.min.css"
    >


    <style>
        @import url('https://fonts.googleapis.com/css2?family=Inter:wght@300;400;600;700&display=swap');

        body {
            font-family: 'Inter', sans-serif;
        }

        .gradient-bg {
            background: linear-gradient(
                135deg,
                #1e3a8a 0%,
                #3b82f6 100%
            );
        }

        .prose {
            max-width: none;
        }
    </style>
</head>
<body class="bg-gray-50 text-gray-900">

    <!-- Navigation -->
    <nav class="bg-white shadow-sm py-4 px-6 sticky top-0 z-50">
        <div class="max-w-7xl mx-auto flex justify-between items-center">
            <a href="/index.html" class="flex items-center space-x-2">
                <img
                    src="/Assets/MeshWaveIcon128.png"
                    alt="MeshWave Logo"
                    class="h-10 w-auto"
                >
                <span class="text-2xl font-bold tracking-tight text-gray-900">
                    MeshWave
                </span>
            </a>

            <div class="hidden md:flex space-x-8 font-medium">
                <a href="/index.html#features"
                   class="hover:text-blue-600 transition">
                    Features
                </a>

                <a href="/documentation"
                   class="text-blue-600 border-b-2 border-blue-600 pb-1">
                    Documentation
                </a>
            </div>
        </div>
    </nav>

    <!-- Hero -->
    <header class="gradient-bg text-white py-24 px-6">
        <div class="max-w-5xl mx-auto text-center">
            <h1 class="text-5xl md:text-7xl font-extrabold mb-6">
                ${escapeHtml(source)}
            </h1>

            <p class="text-xl md:text-2xl opacity-90">
                MeshWave Technical Documentation
            </p>
        </div>
    </header>

    <!-- Content -->
    <section class="py-20 px-6 bg-white">
        <div class="max-w-5xl mx-auto">
            <div class="prose lg:prose-lg max-w-none">
                ${htmlContent}
            </div>
        </div>
    </section>

</body>
</html>
        `;

        res.send(finalHtml);
    });
});

// Start server
app.listen(PORT, () => {
    console.log(`Server running at http://localhost:${PORT}`);
});