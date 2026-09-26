/*eslint-disable*/
var path = require("path")
var webpack = require("webpack")

const paths = require('react-scripts/config/paths')
// PROTO_BUILD_OUT redirects the build so a prototype branch never overwrites the Windows Statics.
paths.appBuild = process.env.PROTO_BUILD_OUT
    ? path.resolve(process.env.PROTO_BUILD_OUT)
    : path.join(path.dirname(paths.appBuild),'../Typedown/Resources/Statics')

module.exports = function override(config, env) {
    const overrideConfig = {
        ...config,
        module: {
            ...config.module,
            rules: [
                ...config.module.rules,
                {
                    test: require.resolve(path.join(__dirname, './src/assets/libs/snap.svg-min.js')),
                    use: 'imports-loader?this=>window,fix=>module.exports=0'
                },
                // ProseMirror / Milkdown ship ESM with fully-specified imports that webpack 5 under CRA rejects.
                {
                    test: /\.m?js$/,
                    resolve: { fullySpecified: false }
                }
            ]
        },
        resolve: {
            ...config.resolve,
            alias: {
                ...config.resolve.alias,
                snapsvg: path.join(__dirname, './src/assets/libs/snap.svg-min.js')
            },
        },
        plugins: [
            ...config.plugins,
            new webpack.optimize.LimitChunkCountPlugin({
                maxChunks: 1
            }),
            new webpack.ProvidePlugin({
                process: 'process/browser',
            }),
        ],
        // Milkdown/ProseMirror ship modern ESM that CRA5's Terser (ES5) cannot minify. This is a prototype
        // branch, so skip minification — bundle size does not matter for a feel/perf test.
        optimization: {
            ...config.optimization,
            minimize: false,
        },
    }

    return overrideConfig;
}