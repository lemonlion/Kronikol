// Generated from: features\kronikol-demo.feature
import { test } from "../../steps/fixtures.ts";

test.describe('Kronikol demo feature', () => {

  test.beforeEach('Background', async ({ Given, catalogue }, testInfo) => { if (testInfo.error) return;
    await Given('the catalogue is loaded', null, { catalogue }); 
  });
  
  test('A simple passing scenario', { tag: ['@feature-tag', '@category:demo', '@happy-path', '@endpoint:/api/orders'] }, async ({ Given, When, Then, And, But }) => { 
    await Given('a customer named "Ada"'); 
    await When('the customer places an order'); 
    await And('the customer opens the "overview" page'); 
    await Then('the order is confirmed'); 
    await But('the order is confirmed'); 
  });

  test.describe('Orders must be validated', () => {

    test('A scenario with a data table and a doc string', { tag: ['@feature-tag', '@category:demo', '@category:validation'] }, async ({ Given, When, Then }) => { 
      await Given('the following order lines:', {"dataTable":{"rows":[{"cells":[{"value":"sku"},{"value":"quantity"},{"value":"price"}]},{"cells":[{"value":"APPLE-1"},{"value":"2"},{"value":"1.50"}]},{"cells":[{"value":"PEAR-7"},{"value":"1"},{"value":"2.25"}]}]}}); 
      await When('the payload is submitted:', {"docString":{"content":"{ \"channel\": \"web\", \"currency\": \"GBP\" }","mediaType":"json"}}); 
      await Then('the order is confirmed'); 
    });

    test('A failing scenario', { tag: ['@feature-tag', '@category:demo', '@failing'] }, async ({ Given, When, Then }) => { 
      await Given('a customer named "Ada"'); 
      await When('the step blows up'); 
      await Then('the order is confirmed'); 
    });

    test.describe('An outline over pages', () => {

      test('Example #1', { tag: ['@feature-tag', '@category:demo'] }, async ({ Given, When, Then }) => { 
        await Given('a customer named "Ada"'); 
        await When('the customer opens the "overview" page'); 
        await Then('the order is confirmed'); 
      });

      test('Example #2', { tag: ['@feature-tag', '@category:demo'] }, async ({ Given, When, Then }) => { 
        await Given('a customer named "Grace"'); 
        await When('the customer opens the "customers" page'); 
        await Then('the order is confirmed'); 
      });

    });

  });

});

// == technical section ==

test.beforeEach('BeforeEach Hooks', ({ $runScenarioHooks }) => $runScenarioHooks('before', {  }));
test.afterEach('AfterEach Hooks', ({ $runScenarioHooks }) => $runScenarioHooks('after', {  }));

test.use({
  $test: [({}, use) => use(test), { scope: 'test', box: true }],
  $uri: [({}, use) => use('features\\kronikol-demo.feature'), { scope: 'test', box: true }],
  $bddFileData: [({}, use) => use(bddFileData), { scope: "test", box: true }],
});

const bddFileData = [ // bdd-data-start
  {"pwTestLine":10,"pickleLine":11,"tags":["@feature-tag","@category:demo","@happy-path","@endpoint:/api/orders"],"steps":[{"pwStepLine":7,"gherkinStepLine":8,"keywordType":"Context","textWithKeyword":"Given the catalogue is loaded","isBg":true,"stepMatchArguments":[]},{"pwStepLine":11,"gherkinStepLine":15,"keywordType":"Context","textWithKeyword":"Given a customer named \"Ada\"","stepMatchArguments":[{"group":{"start":17,"value":"\"Ada\"","children":[{"start":18,"value":"Ada","children":[{}]},{"children":[{}]}]},"parameterTypeName":"string"}]},{"pwStepLine":12,"gherkinStepLine":16,"keywordType":"Action","textWithKeyword":"When the customer places an order","stepMatchArguments":[]},{"pwStepLine":13,"gherkinStepLine":17,"keywordType":"Action","textWithKeyword":"And the customer opens the \"overview\" page","stepMatchArguments":[{"group":{"start":23,"value":"\"overview\"","children":[{"start":24,"value":"overview","children":[{}]},{"children":[{}]}]},"parameterTypeName":"string"}]},{"pwStepLine":14,"gherkinStepLine":18,"keywordType":"Outcome","textWithKeyword":"Then the order is confirmed","stepMatchArguments":[]},{"pwStepLine":15,"gherkinStepLine":19,"keywordType":"Outcome","textWithKeyword":"But the order is confirmed","stepMatchArguments":[]}]},
  {"pwTestLine":20,"pickleLine":24,"tags":["@feature-tag","@category:demo","@category:validation"],"steps":[{"pwStepLine":7,"gherkinStepLine":8,"keywordType":"Context","textWithKeyword":"Given the catalogue is loaded","isBg":true,"stepMatchArguments":[]},{"pwStepLine":21,"gherkinStepLine":25,"keywordType":"Context","textWithKeyword":"Given the following order lines:","stepMatchArguments":[]},{"pwStepLine":22,"gherkinStepLine":29,"keywordType":"Action","textWithKeyword":"When the payload is submitted:","stepMatchArguments":[]},{"pwStepLine":23,"gherkinStepLine":33,"keywordType":"Outcome","textWithKeyword":"Then the order is confirmed","stepMatchArguments":[]}]},
  {"pwTestLine":26,"pickleLine":36,"tags":["@feature-tag","@category:demo","@failing"],"steps":[{"pwStepLine":7,"gherkinStepLine":8,"keywordType":"Context","textWithKeyword":"Given the catalogue is loaded","isBg":true,"stepMatchArguments":[]},{"pwStepLine":27,"gherkinStepLine":37,"keywordType":"Context","textWithKeyword":"Given a customer named \"Ada\"","stepMatchArguments":[{"group":{"start":17,"value":"\"Ada\"","children":[{"start":18,"value":"Ada","children":[{}]},{"children":[{}]}]},"parameterTypeName":"string"}]},{"pwStepLine":28,"gherkinStepLine":38,"keywordType":"Action","textWithKeyword":"When the step blows up","stepMatchArguments":[]},{"pwStepLine":29,"gherkinStepLine":39,"keywordType":"Outcome","textWithKeyword":"Then the order is confirmed","stepMatchArguments":[]}]},
  {"pwTestLine":34,"pickleLine":49,"tags":["@feature-tag","@category:demo"],"steps":[{"pwStepLine":7,"gherkinStepLine":8,"keywordType":"Context","textWithKeyword":"Given the catalogue is loaded","isBg":true,"stepMatchArguments":[]},{"pwStepLine":35,"gherkinStepLine":43,"keywordType":"Context","textWithKeyword":"Given a customer named \"Ada\"","stepMatchArguments":[{"group":{"start":17,"value":"\"Ada\"","children":[{"start":18,"value":"Ada","children":[{}]},{"children":[{}]}]},"parameterTypeName":"string"}]},{"pwStepLine":36,"gherkinStepLine":44,"keywordType":"Action","textWithKeyword":"When the customer opens the \"overview\" page","stepMatchArguments":[{"group":{"start":23,"value":"\"overview\"","children":[{"start":24,"value":"overview","children":[{}]},{"children":[{}]}]},"parameterTypeName":"string"}]},{"pwStepLine":37,"gherkinStepLine":45,"keywordType":"Outcome","textWithKeyword":"Then the order is confirmed","stepMatchArguments":[]}]},
  {"pwTestLine":40,"pickleLine":50,"tags":["@feature-tag","@category:demo"],"steps":[{"pwStepLine":7,"gherkinStepLine":8,"keywordType":"Context","textWithKeyword":"Given the catalogue is loaded","isBg":true,"stepMatchArguments":[]},{"pwStepLine":41,"gherkinStepLine":43,"keywordType":"Context","textWithKeyword":"Given a customer named \"Grace\"","stepMatchArguments":[{"group":{"start":17,"value":"\"Grace\"","children":[{"start":18,"value":"Grace","children":[{}]},{"children":[{}]}]},"parameterTypeName":"string"}]},{"pwStepLine":42,"gherkinStepLine":44,"keywordType":"Action","textWithKeyword":"When the customer opens the \"customers\" page","stepMatchArguments":[{"group":{"start":23,"value":"\"customers\"","children":[{"start":24,"value":"customers","children":[{}]},{"children":[{}]}]},"parameterTypeName":"string"}]},{"pwStepLine":43,"gherkinStepLine":45,"keywordType":"Outcome","textWithKeyword":"Then the order is confirmed","stepMatchArguments":[]}]},
]; // bdd-data-end