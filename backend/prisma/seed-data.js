// Shannana Foods — seed data (single source for prisma/seed.js and scripts/verify-seed.js).
// Mirrors the 5 categories and 20 products in frontend/js/main.js (names, descriptions, categories,
// images, tones and order). Slugs are English kebab-case because the DB CHECK constraint requires
// ^[a-z0-9]+(-[a-z0-9]+)*$. sortOrder uses steps of 10 so items can be inserted between later.

export const categories = [
  { name: "صلصات", nameEn: "Sauces", slug: "sauces", sortOrder: 10 },
  { name: "تتبيلات", nameEn: "Seasonings", slug: "seasonings", sortOrder: 20 },
  { name: "معلبات", nameEn: "Canned Goods", slug: "canned", sortOrder: 30 },
  { name: "مربيات", nameEn: "Jams", slug: "jams", sortOrder: 40 },
  { name: "حبوب وبقوليات", nameEn: "Grains & Legumes", slug: "grains-legumes", sortOrder: 50 },
];

export const products = [
  { name: "كاتشب الطماطم", slug: "ketchup", nameEn: "Tomato Ketchup", descriptionEn: "Rich red ketchup with real tomato flavor", description: "أحمر غني بطعم الطماطم الحقيقي", category: "sauces", imageUrl: "/img/Ketchup.png", tone: "red", sortOrder: 10 },
  { name: "صلصة حارة", slug: "hot-sauce", nameEn: "Hot Sauce", descriptionEn: "Balanced heat that brings any dish to life", description: "حرارة متوازنة تُشعل أي طبق", category: "sauces", imageUrl: "/img/HotSauce.png", tone: "red", sortOrder: 20 },
  { name: "صلصة البيتزا", slug: "pizza-sauce", nameEn: "Pizza Sauce", descriptionEn: "A rich flavor that completes your pizza", description: "نكهة غنية تكمل طعم البيتزا", category: "sauces", imageUrl: "/img/HeroProduct.png", tone: "red", sortOrder: 30 },
  { name: "طحينة", slug: "tahini", nameEn: "Tahini", descriptionEn: "Smooth texture and authentic flavor", description: "قوام ناعم ونكهة أصيلة", category: "sauces", imageUrl: "/img/Tahini.png", tone: "gold", sortOrder: 40 },
  { name: "دبس الرمان", slug: "pomegranate-molasses", nameEn: "Pomegranate Molasses", descriptionEn: "Sweet and tangy with an authentic Eastern character", description: "حلاوة وحموضة بطابع شرقي أصيل", category: "seasonings", imageUrl: "/img/PomegranateMolasses.png", tone: "rose", sortOrder: 50 },
  { name: "خل أبيض", slug: "white-vinegar", nameEn: "White Vinegar", descriptionEn: "Purity and quality in every drop", description: "نقاء وجودة في كل قطرة", category: "seasonings", imageUrl: "/img/WhiteVinegar.png", tone: "green", sortOrder: 60 },
  { name: "خل التفاح", slug: "apple-vinegar", nameEn: "Apple Vinegar", descriptionEn: "A refreshing flavor for everyday use", description: "نكهة منعشة للاستخدام اليومي", category: "seasonings", imageUrl: "/img/AppleVinegar.png", tone: "gold", sortOrder: 70 },
  { name: "ذرة حلوة", slug: "corn", nameEn: "Sweet Corn", descriptionEn: "Carefully selected kernels", description: "حبات مختارة بعناية", category: "canned", imageUrl: "/img/Corn.png", tone: "gold", sortOrder: 80 },
  { name: "فطر", slug: "mushroom", nameEn: "Mushrooms", descriptionEn: "Rich flavor for a wide range of dishes", description: "نكهة غنية لمجموعة واسعة من الأطباق", category: "canned", imageUrl: "/img/Mushroom.png", tone: "gold", sortOrder: 90 },
  { name: "مربى الفراولة", slug: "strawberry-jam", nameEn: "Strawberry Jam", descriptionEn: "The sweetness of strawberries in a rich texture", description: "حلاوة الفراولة بقوام غني", category: "jams", imageUrl: "/img/StrawberryJam.png", tone: "rose", sortOrder: 100 },
  { name: "مربى المشمش", slug: "apricot-jam", nameEn: "Apricot Jam", descriptionEn: "The natural sweetness of fruit", description: "حلاوة الفاكهة كما هي", category: "jams", imageUrl: "/img/ApricotJam.png", tone: "gold", sortOrder: 110 },
  { name: "معجون الطماطم", slug: "tomato-paste", nameEn: "Tomato Paste", descriptionEn: "Concentrated and rich in tomato flavor", description: "مركّز وغني بطعم الطماطم", category: "sauces", imageUrl: "/img/TomatoPaste.png", tone: "red", sortOrder: 120 },
  { name: "عدس شنانة الأحمر", slug: "red-lentils", nameEn: "Shannana Red Lentils", descriptionEn: "Carefully selected, quick-cooking lentils", description: "حبات مختارة سريعة النضج", category: "grains-legumes", imageUrl: "/img/RedLentils.png", tone: "rose", sortOrder: 130 },
  { name: "عدس شنانة", slug: "lentils", nameEn: "Shannana Lentils", descriptionEn: "Carefully selected lentils for a healthy meal", description: "حبات مختارة بعناية لطبخة صحية", category: "grains-legumes", imageUrl: "/img/Lentils.png", tone: "gold", sortOrder: 140 },
  { name: "برغل شنانة", slug: "bulgur", nameEn: "Shannana Bulgur", descriptionEn: "The perfect texture for the tastiest Eastern dishes", description: "قوام مثالي لأشهى الأطباق الشرقية", category: "grains-legumes", imageUrl: "/img/Bulgur.png", tone: "gold", sortOrder: 150 },
  { name: "سكر شنانة", slug: "sugar", nameEn: "Shannana Sugar", descriptionEn: "Pure sweetness for every use", description: "نقاء وحلاوة تناسب كل استخدام", category: "grains-legumes", imageUrl: "/img/Sugar.png", tone: "green", sortOrder: 160 },
  { name: "حمص شنانة", slug: "hummus", nameEn: "Shannana Chickpeas", descriptionEn: "Excellent-quality chickpeas for everyday use", description: "حبات ممتازة للاستخدام اليومي", category: "grains-legumes", imageUrl: "/img/Hummus.png", tone: "gold", sortOrder: 170 },
  { name: "أرز شنانة", slug: "rice", nameEn: "Shannana Rice", descriptionEn: "Long, pure grains for a perfect dish", description: "حبات طويلة ونقية لطبخة مثالية", category: "grains-legumes", imageUrl: "/img/Rice.png", tone: "green", sortOrder: 180 },
  { name: "فريكة شنانة", slug: "freekeh", nameEn: "Shannana Freekeh", descriptionEn: "Authentic flavor from the heart of the land", description: "نكهة أصيلة من قلب الأرض", category: "grains-legumes", imageUrl: "/img/Freekeh.png", tone: "green", sortOrder: 190 },
  { name: "بديل الليمون", slug: "lemon-substitute", nameEn: "Lemon Substitute", descriptionEn: "Refreshing tartness for everyday use", description: "حموضة منعشة للاستخدام اليومي", category: "seasonings", imageUrl: "/img/Lemon.png", tone: "green", sortOrder: 200 },
];
