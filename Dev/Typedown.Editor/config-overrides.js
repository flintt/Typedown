/*eslint-disable*/
var path = require("path")
var webpack = require("webpack")

const paths = require('react-scripts/config/paths')
// Default output is the Windows app's Statics (unchanged). CM6_BUILD_OUT redirects the build elsewhere so
// the migration branch can build without overwriting the Windows bundle. Default path preserved => Windows
// build behaviour is identical when the env var is unset.
paths.appBuild = process.env.CM6_BUILD_OUT
    ? path.resolve(process.env.CM6_BUILD_OUT)
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
        ]
    }

    return overrideConfig;
}