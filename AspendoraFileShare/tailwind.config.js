/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Components/**/*.razor",
    "./wwwroot/**/*.html",
  ],
  theme: {
    extend: {
      colors: {
        'aspendora': {
          DEFAULT: '#660000',
          dark: '#4d0000',
          light: '#800000',
        }
      }
    },
  },
  plugins: [],
}
